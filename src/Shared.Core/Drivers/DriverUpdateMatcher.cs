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
    /// Vendor-only and class/function-only IDs are shared by unrelated
    /// devices. Do not use generic PCI/USB/audio/DCH SWC tokens as specific
    /// WUA-to-PnP or INF-to-device matches. These buses require a strictly
    /// parsed public bus+vendor+model key. Other Windows enumerators keep
    /// their pre-existing exact-ID semantics.
    /// </summary>
    public static bool IsCorrelatableHardwareId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        var value = id.Trim();
        var requiresSpecificModel =
            value.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(@"HDAUDIO\", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(@"INTELAUDIO\", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(@"SWC\", StringComparison.OrdinalIgnoreCase);
        if (!requiresSpecificModel)
            return true;

        // Reuse the strict public-model grammar already used by vendor
        // discovery and WUA ID selection. The rule does not strip SUBSYS,
        // REV or compatible suffixes to invent equality: the matcher
        // still requires the *original* complete PnP ID to be identical.
        return MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(value)
            is not null;
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
