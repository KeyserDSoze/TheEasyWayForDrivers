using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Abstractions;

public interface IOemDriverProvider
{
    string ProviderId { get; }

    Task<OemProviderStatus> GetStatusAsync(
        IReadOnlyList<DriverInfo> drivers,
        CancellationToken cancellationToken);
}
