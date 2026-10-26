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
        return !update.IsHidden &&
               !update.IsAdvancedCandidate &&
               Guid.TryParse(update.Id, out _);
    }

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
