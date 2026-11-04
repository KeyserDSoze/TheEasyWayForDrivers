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
        var firmwareText = string.Join(" ", new[]
        {
            update.Title,
            update.DriverClass ?? string.Empty
        });

        if (firmwareText.Contains("Firmware", StringComparison.OrdinalIgnoreCase) ||
            firmwareText.Contains("BIOS", StringComparison.OrdinalIgnoreCase) ||
            firmwareText.Contains("UEFI", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !update.IsHidden &&
               !update.IsAdvancedCandidate &&
               Guid.TryParse(update.Id, out _) &&
               // An allowlist of names emitted by the actual WUA provider.
               // Microsoft Update is a registered WUA service, not an OEM
               // download. Unknown origins cannot be quick-installed.
               IsKnownWindowsUpdateSource(update.SearchSource);
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
        return updates
            .Where(IsEligible)
            .Select(update => update.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
