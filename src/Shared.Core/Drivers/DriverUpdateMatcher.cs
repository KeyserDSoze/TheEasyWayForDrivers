using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Hardware-ID equality is stronger evidence than compatible-ID equality.
/// Neither kind proves that installation is safe for a specific PC.
/// </summary>
public static class DriverUpdateMatcher
{
    /// <summary>
    /// PCI class-only and vendor-only IDs are shared by unrelated devices.
    /// They must not correlate WUA driver entries to one specific PnP device.
    /// A PCI WUA ID requires a strictly parsed public VEN+DEV model;
    /// other buses retain existing exact-equality matching semantics.
    /// </summary>
    public static bool IsCorrelatableHardwareId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        var value = id.Trim();
        return !value.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) ||
            MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(value)?
                .StartsWith(@"PCI\VEN_", StringComparison.Ordinal) == true;
    }

    public static int MatchStrength(DriverInfo driver, DriverUpdateInfo update)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(update);

        return MatchStrength(driver, update.HardwareId, update.AdditionalHardwareIds);
    }

    /// <summary>
    /// Shared WUA association check for Home and the unified Discovery+ view.
    /// Both surfaces honor the same cap and ID-priority semantics. Association
    /// is diagnostic evidence, never installation authorization.
    /// </summary>
    public static int MatchStrength(
        DriverInfo driver, string? primaryHardwareId,
        IReadOnlyList<string>? additionalHardwareIds)
    {
        ArgumentNullException.ThrowIfNull(driver);

        // A WUA update can have several applicable entries, each declaring
        // its own Hardware/Compatible ID. They all describe the SAME update,
        // not extra packages or a guarantee that Windows would select it.
        var declaredIds = new[] { primaryHardwareId }
            .Concat((additionalHardwareIds ?? []).Take(WuaDriverEntryLimits.MaximumAdditionalIds))
            .Where(IsCorrelatableHardwareId)
            .Select(id => id!.Trim())
            .ToArray();

        if (declaredIds.Length == 0)
            return 0;

        // Any exact hardware match outranks a compatible-ID match, even
        // when the matching IDs appear in a different order in WUA.
        if (declaredIds.Any(id => driver.HardwareIds.Any(hardware =>
            string.Equals(hardware?.Trim(), id, StringComparison.OrdinalIgnoreCase))))
            return 2;

        return declaredIds.Any(id => driver.CompatibleIds.Any(compatible =>
            string.Equals(compatible?.Trim(), id, StringComparison.OrdinalIgnoreCase)))
            ? 1 : 0;
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
