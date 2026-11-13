using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// The Desktop tray announces only ordinary, confirmed-origin WUA driver
/// GUIDs accepted by the shared Update All safety policy. The fingerprint
/// is an ID-set indicator, not a version or installability guarantee.
/// </summary>
public static class DriverUpdateFingerprint
{
    public static IReadOnlyList<DriverUpdateInfo> SelectNotifiable(
        IEnumerable<DriverUpdateInfo> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        var candidates = updates.ToArray();

        // Do not claim firmware, BrowseOnly/optional, hidden, advanced or
        // foreign-source metadata is a new ordinary driver. A trusted WUA
        // representation of the SAME GUID can veto a different otherwise
        // ordinary representation, regardless of GUID text format/order.
        var approvedIds = QuickUpdatePolicy.AllOrdinaryIds(candidates)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates
            .Where(update => QuickUpdatePolicy.IsEligible(update) &&
                approvedIds.Contains(Guid.Parse(update.Id).ToString("D")))
            .DistinctBy(update => Guid.Parse(update.Id))
            .ToArray();
    }

    public static string Create(IEnumerable<DriverUpdateInfo> updates)
    {
        return string.Join("|", SelectNotifiable(updates)
            .Select(update => Guid.Parse(update.Id).ToString("D"))
            .OrderBy(id => id, StringComparer.Ordinal));
    }
}
