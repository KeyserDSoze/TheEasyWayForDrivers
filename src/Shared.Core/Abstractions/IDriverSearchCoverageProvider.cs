using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Abstractions;

/// <summary>
/// Extended read-only WUA discovery. Kept separate from IDriverUpdateProvider to
/// preserve existing client/test contracts and installation safety boundaries.
/// </summary>
public interface IDriverSearchCoverageProvider : IDriverUpdateProvider
{
    Task<DriverSearchReport> SearchWithCoverageAsync(
        DriverSearchMode mode,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken);
}
