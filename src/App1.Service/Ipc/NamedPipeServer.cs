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
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(15);
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
                // Exception.Message can include paths, device IDs and
                // WUA/COM payload text. Keep the privileged service log
                // useful without persisting local identifiers or a stack.
                logger.LogError(
                    "Named-pipe request failed ({ExceptionType}, HRESULT 0x{HResult:X8}).",
                    exception.GetType().Name, exception.HResult);
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
                "Rejected IPC client process {ProcessId}.",
                clientProcess.ProcessId);

            await WriteAsync(
                writer,
                IpcMessage.Error(
                    "IPC client is not the installed TheEasyWayForDrivers desktop application."),
                cancellationToken);

            return;
        }

        // Enforce the bound WHILE reading, rather than allocating an entire
        // potentially unbounded JSON line before checking its length.
        BoundedIpcRequestReader.ReadResult requestLine;
        try
        {
            requestLine = await IpcRequestReadPolicy.ReadAsync(
                reader, MaximumRequestCharacters, RequestReadTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            // A stalled client must not monopolize the only server pipe.
            // Never log partial JSON, identifiers or driver data.
            logger.LogWarning(
                "IPC request timed out for client process {ProcessId}.",
                clientProcess.ProcessId);
            await WriteAsync(
                writer, IpcMessage.Error("IPC request timed out."),
                cancellationToken);
            return;
        }
        if (requestLine.TooLarge)
        {
            logger.LogWarning(
                "Rejected oversized IPC request from process {ProcessId}.",
                clientProcess.ProcessId);

            await WriteAsync(
                writer,
                IpcMessage.Error("IPC request is too large."),
                cancellationToken);

            return;
        }

        var line = requestLine.Line;
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        IpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<IpcRequest>(line, JsonOptions);
        }
        catch (JsonException)
        {
            // Bad JSON is a protocol error, not a reason to expose the
            // serializer's path/value/byte position or disconnect silently.
            logger.LogWarning("Rejected malformed IPC JSON request.");
            await WriteAsync(writer,
                IpcMessage.Error(IpcTransportDisclosurePolicy.InvalidRequest),
                cancellationToken);
            return;
        }
        if (request is null)
        {
            await WriteAsync(writer,
                IpcMessage.Error(IpcTransportDisclosurePolicy.InvalidRequest),
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Command) || request.Command.Length > 64)
        {
            await WriteAsync(
                writer,
                IpcMessage.Error(IpcTransportDisclosurePolicy.InvalidRequest),
                cancellationToken);

            return;
        }

        var safeCommand = IpcTransportDisclosurePolicy.SafeCommandLabel(
            request.Command);
        logger.LogInformation(
            "IPC command {Command} received from process {ProcessId}.",
            safeCommand,
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
                "Invalid IPC command {Command} ({ExceptionType}, HRESULT 0x{HResult:X8}).",
                safeCommand, exception.GetType().Name, exception.HResult);

            await WriteAsync(
                writer,
                IpcMessage.Error(IpcTransportDisclosurePolicy.ToClientError(exception)),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "IPC command {Command} failed ({ExceptionType}, HRESULT 0x{HResult:X8}).",
                safeCommand, exception.GetType().Name, exception.HResult);

            await WriteAsync(
                writer,
                IpcMessage.Error(IpcTransportDisclosurePolicy.ToClientError(exception)),
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
