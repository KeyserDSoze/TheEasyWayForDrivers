using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Limits one-click install actions to ordinary WUA results. OEM metadata
/// and hidden/advanced candidates never become bulk-installable.
/// </summary>
public static class QuickUpdatePolicy
{
    public static bool IsEligible(DriverUpdateInfo update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (IsFirmwareOrBios(update.Title, update.DriverClass))
            return false;

        return !update.IsHidden &&
               !update.IsOptional &&
               !update.IsAdvancedCandidate &&
               Guid.TryParse(update.Id, out _) &&
               // An allowlist of names emitted by the actual WUA provider.
               // Microsoft Update is a registered WUA service, not an OEM
               // download. Unknown origins cannot be quick-installed.
               IsKnownWindowsUpdateSource(update.SearchSource);
    }

    /// <summary>
    /// Shared fail-closed firmware gate. The service must recheck the same
    /// hazard independently of the client, before WUA EULA/download/install.
    /// </summary>
    public static bool IsFirmwareOrBios(string? title, string? driverClass)
    {
        var metadata = string.Join(" ", title ?? "", driverClass ?? "");
        return metadata.Contains("Firmware", StringComparison.OrdinalIgnoreCase) ||
            metadata.Contains("BIOS", StringComparison.OrdinalIgnoreCase) ||
            metadata.Contains("UEFI", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsKnownWindowsUpdateSource(string? source) =>
        source is not null &&
        (source.Equals("Windows configurato", StringComparison.OrdinalIgnoreCase) ||
         source.Equals("Windows Update online", StringComparison.OrdinalIgnoreCase) ||
         source.Equals("Microsoft Update online", StringComparison.OrdinalIgnoreCase) ||
         source.Equals("Windows Update", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> AllOrdinaryIds(
        IEnumerable<DriverUpdateInfo> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        // Defense in depth for old IPC snapshots or callers that supply
        // independently returned WUA passes without merging them first.
        // Case-insensitive string de-dup cannot equate B/N/P/D GUID forms;
        // filtering eligible rows BEFORE grouping can also discard trusted
        // BrowseOnly or firmware evidence for that very same UpdateID.
        // Select one canonical GUID only when at least one trusted ordinary
        // pass recommends it AND no trusted pass vetoes bulk install.
        // Unknown OEM/manual sources cannot recommend or poison a WUA GUID.
        return updates
            .Where(WindowsDriverCandidateMerge.IsKnownWuaGuid)
            .GroupBy(update => Guid.Parse(update.Id))
            .Where(group =>
                group.Any(IsEligible) &&
                !group.Any(update =>
                    update.IsOptional ||
                    IsFirmwareOrBios(update.Title, update.DriverClass)))
            .Select(group => group.Key.ToString("D"))
            .ToArray();
    }
}
