using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Desktop.Services;

public sealed class DriverServiceClient
{
    private const string PipeName = "TheEasyWayForDrivers.Service.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<DriverInfo>> ScanAsync(CancellationToken cancellationToken) =>
        SendForResultAsync<IReadOnlyList<DriverInfo>>(
            new IpcRequest("scan", null),
            null,
            cancellationToken);

    public Task<IReadOnlyList<DriverUpdateInfo>> SearchUpdatesAsync(CancellationToken cancellationToken) =>
        SendForResultAsync<IReadOnlyList<DriverUpdateInfo>>(
            new IpcRequest("search-updates", null),
            null,
            cancellationToken);

    public Task<DriverInstallResult> InstallUpdatesAsync(
        IReadOnlyCollection<string> updateIds,
        Action<OperationProgress> progress,
        CancellationToken cancellationToken) =>
        SendForResultAsync<DriverInstallResult>(
            new IpcRequest("install-updates", updateIds.ToArray()),
            progress,
            cancellationToken);

    public Task<ServiceDiagnosticsInfo> GetDiagnosticsAsync(CancellationToken cancellationToken) =>
        SendForResultAsync<ServiceDiagnosticsInfo>(
            new IpcRequest("diagnostics", null),
            null,
            cancellationToken);

    public async Task<AppUpdateInfo?> CheckAppUpdateAsync(CancellationToken cancellationToken)
    {
        var data = await SendForElementAsync(
            new IpcRequest("check-app-update", null),
            null,
            cancellationToken);

        return data.TryGetProperty("tagName", out _)
            ? data.Deserialize<AppUpdateInfo>(JsonOptions)
            : null;
    }

    private static async Task<T> SendForResultAsync<T>(
        IpcRequest request,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var data = await SendForElementAsync(request, progress, cancellationToken);

        return data.Deserialize<T>(JsonOptions)
            ?? throw new InvalidDataException("The driver service returned an empty result.");
    }

    private static async Task<JsonElement> SendForElementAsync(
        IpcRequest request,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);

        await pipe.ConnectAsync(3000, cancellationToken);

        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        await writer.WriteLineAsync(requestJson.AsMemory(), cancellationToken);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new IOException("The driver service closed the IPC connection.");
            }

            using var message = JsonDocument.Parse(line);
            var root = message.RootElement;
            var kind = root.GetProperty("kind").GetString();

            if (kind == "error")
            {
                throw new InvalidOperationException(
                    root.GetProperty("errorMessage").GetString() ?? "Driver service error.");
            }

            if (kind == "progress")
            {
                if (progress is not null)
                {
                    var value = root.GetProperty("data").Deserialize<OperationProgress>(JsonOptions);
                    if (value is not null)
                    {
                        progress(value);
                    }
                }

                continue;
            }

            if (kind == "result")
            {
                return root.GetProperty("data").Clone();
            }
        }
    }

    private sealed record IpcRequest(string Command, string[]? UpdateIds);
}
