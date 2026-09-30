using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Abstractions;

public interface IAppUpdateProvider
{
    Task<AppUpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken);
}
