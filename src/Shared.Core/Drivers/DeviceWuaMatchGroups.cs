using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Correlates ALL confirmed WUA Type=Driver candidates with a PnP device,
/// then separates ordinary one-click-eligible updates from advanced,
/// hidden and other review-only evidence. A matching ID does not imply
/// that the package is newer, OEM-compatible or safe to install.
/// WUA Type=Software/partial-hint data never enters this classifier.
/// </summary>
public static class DeviceWuaMatchGroups
{
    public sealed record Result(
        IReadOnlyList<DriverUpdateInfo> Ordinary,
        IReadOnlyList<DriverUpdateInfo> ReviewOnly)
    {
        public int TotalCorrelated => Ordinary.Count + ReviewOnly.Count;
    }

    public static Result Create(
        DriverInfo device, IEnumerable<DriverUpdateInfo> confirmedDriverUpdates)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(confirmedDriverUpdates);

        // Match once, across the entire WUA Type=Driver result set. The
        // same GUID returned by multiple passes must never inflate counts.
        var matches = DriverUpdateMatcher.FindMatches(
            device, confirmedDriverUpdates.Where(update =>
                Guid.TryParse(update.Id, out _)));

        // If a GUID appears in more than one representation, the verified
        // ordinary representation wins. Advanced/hidden copies cannot cause
        // an installable offer to be presented as review-only (or vice versa).
        var ordinary = matches
            .Where(QuickUpdatePolicy.IsEligible)
            .DistinctBy(update => update.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var ordinaryIds = ordinary.Select(update => update.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reviewOnly = matches
            .Where(update => !QuickUpdatePolicy.IsEligible(update) &&
                !ordinaryIds.Contains(update.Id))
            .DistinctBy(update => update.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new Result(ordinary, reviewOnly);
    }
}
