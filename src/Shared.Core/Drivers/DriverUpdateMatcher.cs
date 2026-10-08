using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Hardware-ID equality is stronger evidence than compatible-ID equality.
/// Neither kind proves that installation is safe for a specific PC.
/// </summary>
public static class DriverUpdateMatcher
{
    public static int MatchStrength(DriverInfo driver, DriverUpdateInfo update)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(update);

        if (string.IsNullOrWhiteSpace(update.HardwareId))
            return 0;

        var id = update.HardwareId.Trim();

        if (driver.HardwareIds.Any(hardware =>
            string.Equals(hardware?.Trim(), id, StringComparison.OrdinalIgnoreCase)))
            return 2;

        return driver.CompatibleIds.Any(compatible =>
            string.Equals(compatible?.Trim(), id, StringComparison.OrdinalIgnoreCase))
            ? 1
            : 0;
    }

    public static bool IsMatch(DriverInfo driver, DriverUpdateInfo update) =>
        MatchStrength(driver, update) > 0;

    public static IReadOnlyList<DriverUpdateInfo> FindMatches(
        DriverInfo driver,
        IEnumerable<DriverUpdateInfo> updates)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(updates);

        return updates
            .Select(update => new { Update = update, Strength = MatchStrength(driver, update) })
            .Where(candidate => candidate.Strength > 0)
            .OrderByDescending(candidate => candidate.Strength)
            .ThenByDescending(candidate => candidate.Update.DriverDate)
            .ThenBy(candidate => candidate.Update.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(candidate => candidate.Update)
            .ToArray();
    }
}
