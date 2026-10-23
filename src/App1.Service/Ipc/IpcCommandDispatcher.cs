using System.Reflection;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Ipc;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.ServiceApp.Infrastructure;
using TheEasyWayForDrivers.ServiceApp.Services;

namespace TheEasyWayForDrivers.ServiceApp.Ipc;

/// <summary>
/// Executes validated IPC operations independently of pipe transport and client authorization.
/// The NamedPipeServer MUST authorize the installed desktop process before calling this class.
/// </summary>
public sealed class IpcCommandDispatcher(
    IDriverInventory driverInventory,
    IDriverUpdateProvider updateProvider,
    OemProviderCoordinator oemProviderCoordinator,
    IAppUpdateProvider appUpdateProvider,
    ServiceDiagnosticsReader diagnosticsReader)
{
    public async Task<object> DispatchAsync(
        IpcRequest request,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return request.Command switch
        {
            "scan" => await driverInventory.GetInstalledDriversAsync(cancellationToken),
            "search-updates" => await updateProvider.SearchAsync(
                ParseSearchMode(request.SearchMode),
                progress,
                cancellationToken),
            "search-report" => await SearchReportAsync(
                ParseSearchMode(request.SearchMode), progress, cancellationToken),
            "install-updates" => await updateProvider.InstallAsync(
                IpcInputValidator.ValidateUpdateIds(request.UpdateIds),
                progress,
                cancellationToken),
            "check-app-update" => await CheckAppUpdateAsync(cancellationToken),
            "oem-providers" => await oemProviderCoordinator.GetStatusesAsync(cancellationToken),
            "diagnostics" => diagnosticsReader.Read(),
            _ => throw new InvalidOperationException(
                $"Unknown command '{request.Command}'.")
        };
    }

    private Task<DriverSearchReport> SearchReportAsync(
        DriverSearchMode mode,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (updateProvider is not IDriverSearchCoverageProvider coverageProvider)
        {
            throw new NotSupportedException(
                "The configured Windows Update provider cannot report search coverage.");
        }

        return coverageProvider.SearchWithCoverageAsync(mode, progress, cancellationToken);
    }

    private async Task<object> CheckAppUpdateAsync(CancellationToken cancellationToken)
    {
        var currentVersion =
            Assembly.GetExecutingAssembly().GetName().Version ??
            new Version(0, 0, 1);

        return (object?)await appUpdateProvider.CheckAsync(currentVersion, cancellationToken)
            ?? AppUpdateUnavailable.Instance;
    }

    private static DriverSearchMode ParseSearchMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DriverSearchMode.Recommended;
        }

        if (Enum.TryParse<DriverSearchMode>(value, ignoreCase: true, out var mode) &&
            Enum.IsDefined(mode))
        {
            return mode;
        }

        throw new ArgumentException("Invalid driver search mode.");
    }

    private sealed record AppUpdateUnavailable
    {
        public static AppUpdateUnavailable Instance { get; } = new();
    }
}

/// <summary>
/// JSON request shape shared by the IPC pipe boundary and testable command dispatcher.
/// Changes must remain compatible with App2.Desktop's request serializer.
/// </summary>
public sealed record IpcRequest(
    string Command,
    string[]? UpdateIds,
    string? SearchMode = null);
