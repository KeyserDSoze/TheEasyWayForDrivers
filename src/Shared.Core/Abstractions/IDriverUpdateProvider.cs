using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Abstractions;

public interface IDriverUpdateProvider
{
    Task<IReadOnlyList<DriverUpdateInfo>> SearchAsync(
        DriverSearchMode mode,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken);

    Task<DriverInstallResult> InstallAsync(
        IReadOnlyCollection<string> updateIds,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken);
}
