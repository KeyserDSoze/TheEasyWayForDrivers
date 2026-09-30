using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Abstractions;

public interface IDriverInventory
{
    Task<IReadOnlyList<DriverInfo>> GetInstalledDriversAsync(CancellationToken cancellationToken);
}
