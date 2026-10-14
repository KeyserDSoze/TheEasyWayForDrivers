using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.ServiceApp.Ipc;

public sealed class NamedPipeServer(
    IpcCommandDispatcher commandDispatcher,
    ILogger<NamedPipeServer> logger)
{
    public const string PipeName = "TheEasyWayForDrivers.Service.v1";

    private const int MaximumRequestCharacters = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = CreateServerPipe();

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                await HandleClientAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Named-pipe request failed.");
            }
        }
    }

    private async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };

        var clientProcess = ClientProcessVerifier.Inspect(pipe.SafePipeHandle);
        if (!clientProcess.IsAuthorized)
        {
            logger.LogWarning(
                "Rejected IPC client process {ProcessId} at {ExecutablePath}.",
                clientProcess.ProcessId,
                clientProcess.ExecutablePath ?? "unknown");

            await WriteAsync(
                writer,
                IpcMessage.Error(
                    "IPC client is not the installed TheEasyWayForDrivers desktop application."),
                cancellationToken);

            return;
        }

        var line = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (line.Length > MaximumRequestCharacters)
        {
            logger.LogWarning(
                "Rejected oversized IPC request from {ClientIdentity}, process {ProcessId}.",
                GetClientIdentity(pipe),
                clientProcess.ProcessId);

            await WriteAsync(
                writer,
                IpcMessage.Error("IPC request is too large."),
                cancellationToken);

            return;
        }

        var request = JsonSerializer.Deserialize<IpcRequest>(line, JsonOptions)
            ?? throw new InvalidDataException("Invalid IPC request.");

        if (string.IsNullOrWhiteSpace(request.Command) || request.Command.Length > 64)
        {
            await WriteAsync(
                writer,
                IpcMessage.Error("Invalid IPC command."),
                cancellationToken);

            return;
        }

        var clientIdentity = GetClientIdentity(pipe);
        logger.LogInformation(
            "IPC command {Command} received from {ClientIdentity}, process {ProcessId}.",
            request.Command,
            clientIdentity,
            clientProcess.ProcessId);

        try
        {
            var result = await commandDispatcher.DispatchAsync(
                request,
                progress => WriteBlocking(
                    writer,
                    IpcMessage.Progress(progress),
                    cancellationToken),
                cancellationToken);

            await WriteAsync(
                writer,
                IpcMessage.Result(result),
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(
                exception,
                "Rejected invalid IPC command {Command} from {ClientIdentity}.",
                request.Command,
                clientIdentity);

            await WriteAsync(
                writer,
                IpcMessage.Error(exception.Message),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "IPC command {Command} from {ClientIdentity} failed.",
                request.Command,
                clientIdentity);

            await WriteAsync(
                writer,
                IpcMessage.Error(exception.Message),
                cancellationToken);
        }
    }

    private static NamedPipeServerStream CreateServerPipe()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(
            isProtected: true,
            preserveInheritance: false);

        var networkSid =
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null);

        var localSystemSid =
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        var administratorsSid = new SecurityIdentifier(
            WellKnownSidType.BuiltinAdministratorsSid,
            null);

        var interactiveSid =
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null);

        security.AddAccessRule(new PipeAccessRule(
            networkSid,
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        security.AddAccessRule(new PipeAccessRule(
            localSystemSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            administratorsSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            interactiveSid,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            16 * 1024,
            16 * 1024,
            security,
            HandleInheritability.None,
            (PipeAccessRights)0);
    }

    private static string GetClientIdentity(NamedPipeServerStream pipe)
    {
        try
        {
            return pipe.GetImpersonationUserName();
        }
        catch (InvalidOperationException)
        {
            return "unknown";
        }
        catch (IOException)
        {
            return "unknown";
        }
        catch (UnauthorizedAccessException)
        {
            return "unknown";
        }
    }

    private static Task WriteAsync(
        StreamWriter writer,
        IpcMessage message,
        CancellationToken cancellationToken) =>
        writer.WriteLineAsync(
            JsonSerializer.Serialize(message, JsonOptions).AsMemory(),
            cancellationToken);

    private static void WriteBlocking(
        StreamWriter writer,
        IpcMessage message,
        CancellationToken cancellationToken) =>
        WriteAsync(writer, message, cancellationToken)
            .GetAwaiter()
            .GetResult();

    private sealed record IpcMessage(
        string Kind,
        bool Success,
        object? Data,
        string? ErrorMessage)
    {
        public static IpcMessage Result(object data) =>
            new("result", true, data, null);

        public static IpcMessage Progress(OperationProgress progress) =>
            new("progress", true, progress, null);

        public static IpcMessage Error(string message) =>
            new("error", false, null, message);
    }

}
