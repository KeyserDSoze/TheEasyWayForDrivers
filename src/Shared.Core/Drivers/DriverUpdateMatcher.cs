using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

public static class DriverUpdateMatcher
{
    public static bool IsMatch(
        DriverInfo driver,
        DriverUpdateInfo update)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(update);

        if (string.IsNullOrWhiteSpace(update.HardwareId))
        {
            return false;
        }

        return driver.HardwareIds
                   .Concat(driver.CompatibleIds)
                   .Any(id => string.Equals(
                       id?.Trim(),
                       update.HardwareId.Trim(),
                       StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<DriverUpdateInfo> FindMatches(
        DriverInfo driver,
        IEnumerable<DriverUpdateInfo> updates)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(updates);

        return updates
            .Where(update => IsMatch(driver, update))
            .OrderByDescending(update => update.DriverDate)
            .ThenBy(update => update.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
