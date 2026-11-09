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
    /// <summary>
    /// A single, read-only WUA GUID normalization for an entire scan.
    /// Prepare once and call ForDevice for each PnP row. Install eligibility
    /// is still decided only by QuickUpdatePolicy on the merged metadata.
    /// </summary>
    public sealed class Prepared
    {
        private readonly IReadOnlyList<DriverUpdateInfo> _updates;

        internal Prepared(IReadOnlyList<DriverUpdateInfo> updates) =>
            _updates = updates;

        public int UniqueGuidCount => _updates.Count;

        public Result ForDevice(DriverInfo device)
        {
            ArgumentNullException.ThrowIfNull(device);
            return MatchPrepared(device, _updates);
        }
    }

    /// <summary>
    /// Reuse the service's bounded merge: later WUA sources can declare
    /// another PnP ID for the SAME GUID. Ordinary metadata wins advanced,
    /// while unknown-origin, hidden and firmware records remain subject
    /// to the existing QuickUpdatePolicy and install-time WUA recheck.
    /// </summary>
    public static Prepared Prepare(
        IEnumerable<DriverUpdateInfo> confirmedDriverUpdates)
    {
        ArgumentNullException.ThrowIfNull(confirmedDriverUpdates);
        var byGuid = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(byGuid, confirmedDriverUpdates
            .Where(update => Guid.TryParse(update.Id, out _)));
        return new Prepared(byGuid.Values.ToArray());
    }

    public sealed record Result(
        IReadOnlyList<DriverUpdateInfo> Ordinary,
        IReadOnlyList<DriverUpdateInfo> ReviewOnly,
        IReadOnlyList<DriverUpdateInfo>? ModelOnlyReview = null)
    {
        public int TotalCorrelated =>
            Ordinary.Count + ReviewOnly.Count + (ModelOnlyReview?.Count ?? 0);
    }

    public static Result Create(
        DriverInfo device, IEnumerable<DriverUpdateInfo> confirmedDriverUpdates)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(confirmedDriverUpdates);

        return Prepare(confirmedDriverUpdates).ForDevice(device);
    }

    private static Result MatchPrepared(
        DriverInfo device, IReadOnlyList<DriverUpdateInfo> verifiedUpdates)
    {
        // GUIDs and bounded PnP IDs have been normalized exactly once.
        var matches = DriverUpdateMatcher.FindMatches(
            device, verifiedUpdates);

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

        var matchedIds = matches
            .Select(update => update.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Retain additional confirmed WUA GUIDs with a generic public
        // hardware-model token when Win32_PnPEntity did not supply the
        // equivalent Compatible ID. These are model-only REVIEW hints:
        // even an otherwise ordinary WUA entry is not included in
        // Ordinary/PreferredUpdate or device one-click installation.
        var modelOnly = verifiedUpdates
            .Where(update => !matchedIds.Contains(update.Id) &&
                WuaGenericModelCorrelation.IsWeakModelOnlyMatch(device, update))
            .OrderBy(update => update.IsAdvancedCandidate)
            .ThenBy(update => update.IsHidden)
            .ThenBy(update => update.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(update => update.Id, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(update => update.Id, StringComparer.OrdinalIgnoreCase)
            .Take(WuaGenericModelCorrelation.MaximumReviewHintsPerDevice)
            .ToArray();

        return new Result(ordinary, reviewOnly, modelOnly);
    }
}
