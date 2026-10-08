using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Counts PnP problems separately from unverified driver metadata and optional updates.
/// </summary>
public sealed record DeviceAttentionSummary(
    int Missing,
    int WindowsErrors,
    int Unverified,
    int Healthy)
{
    public int Problems => Missing + WindowsErrors;
    public int NeedsReview => Problems + Unverified;

    public static DeviceAttentionSummary Create(IEnumerable<DriverInfo> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        var missing = 0;
        var errors = 0;
        var unverified = 0;
        var healthy = 0;

        foreach (var driver in drivers)
        {
            ArgumentNullException.ThrowIfNull(driver);
            if (driver.IsDriverMissing) missing++;
            else if (driver.HasWindowsProblem) errors++;
            else if (driver.IsDriverUnverified) unverified++;
            else healthy++;
        }

        return new DeviceAttentionSummary(missing, errors, unverified, healthy);
    }
}
