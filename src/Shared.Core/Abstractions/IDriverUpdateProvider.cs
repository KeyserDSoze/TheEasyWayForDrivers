using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Abstractions;

public interface IDriverUpdateProvider
{
    Task<IReadOnlyList<DriverUpdateInfo>> SearchAsync(CancellationToken cancellationToken);

    Task<DriverInstallResult> InstallAsync(
        IReadOnlyCollection<string> updateIds,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken);
}
