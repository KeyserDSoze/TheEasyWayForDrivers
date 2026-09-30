using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.ServiceApp.Ipc;

public sealed class NamedPipeServer(
    IDriverInventory driverInventory,
    IDriverUpdateProvider updateProvider,
    IAppUpdateProvider appUpdateProvider,
    ILogger<NamedPipeServer> logger)
{
    public const string PipeName = "TheEasyWayForDrivers.Service.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

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

    private async Task HandleClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };

        var line = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var request = JsonSerializer.Deserialize<IpcRequest>(line, JsonOptions)
            ?? throw new InvalidDataException("Invalid IPC request.");

        try
        {
            switch (request.Command)
            {
                case "scan":
                    await WriteAsync(
                        writer,
                        IpcMessage.Result(await driverInventory.GetInstalledDriversAsync(cancellationToken)),
                        cancellationToken);
                    break;

                case "search-updates":
                    await WriteAsync(
                        writer,
                        IpcMessage.Result(await updateProvider.SearchAsync(cancellationToken)),
                        cancellationToken);
                    break;

                case "install-updates":
                    var result = await updateProvider.InstallAsync(
                        request.UpdateIds ?? Array.Empty<string>(),
                        progress => WriteBlocking(writer, IpcMessage.Progress(progress), cancellationToken),
                        cancellationToken);

                    await WriteAsync(writer, IpcMessage.Result(result), cancellationToken);
                    break;

                case "check-app-update":
                    var currentVersion =
                        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 1);

                    var appUpdate = await appUpdateProvider.CheckAsync(
                        currentVersion,
                        cancellationToken);

                    await WriteAsync(
                        writer,
                        IpcMessage.Result(appUpdate ?? AppUpdateUnavailable.Instance),
                        cancellationToken);
                    break;

                default:
                    await WriteAsync(
                        writer,
                        IpcMessage.Error($"Unknown command '{request.Command}'."),
                        cancellationToken);
                    break;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "IPC command {Command} failed.", request.Command);
            await WriteAsync(writer, IpcMessage.Error(exception.Message), cancellationToken);
        }
    }

    private static Task WriteAsync(
        StreamWriter writer,
        IpcMessage message,
        CancellationToken cancellationToken) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(message, JsonOptions).AsMemory(), cancellationToken);

    private static void WriteBlocking(
        StreamWriter writer,
        IpcMessage message,
        CancellationToken cancellationToken) =>
        WriteAsync(writer, message, cancellationToken).GetAwaiter().GetResult();

    private sealed record IpcRequest(string Command, string[]? UpdateIds);

    private sealed record IpcMessage(
        string Kind,
        bool Success,
        object? Data,
        string? ErrorMessage)
    {
        public static IpcMessage Result(object data) => new("result", true, data, null);
        public static IpcMessage Progress(OperationProgress progress) => new("progress", true, progress, null);
        public static IpcMessage Error(string message) => new("error", false, null, message);
    }

    private sealed record AppUpdateUnavailable
    {
        public static AppUpdateUnavailable Instance { get; } = new();
    }
}
