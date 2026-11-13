using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Fail-closed presentation boundary for per-device WUA associations supplied
/// by a Desktop scan snapshot. Matching a PnP ID never proves a safe update:
/// installation still needs independent WUA/server-side checks.
/// </summary>
public static class DeviceWuaPresentationGate
{
    public sealed record Result(
        IReadOnlyList<DriverUpdateInfo> Ordinary,
        IReadOnlyList<DriverUpdateInfo> ReviewOnly,
        IReadOnlyList<DriverUpdateInfo> ModelOnlyReview);

    public static Result Filter(
        DriverInfo device,
        IReadOnlyList<DriverUpdateInfo> ordinary,
        IReadOnlyList<DriverUpdateInfo>? reviewOnly = null,
        IReadOnlyList<DriverUpdateInfo>? modelOnlyReview = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(ordinary);

        // Only ordinary, confirmed-origin GUIDs with an exact PnP Hardware
        // or Compatible ID can enable a per-device update in the Desktop.
        var allowed = ordinary
            .Where(update => QuickUpdatePolicy.IsEligible(update) &&
                DriverUpdateMatcher.MatchStrength(device, update) > 0)
            .DistinctBy(update => Guid.Parse(update.Id))
            .ToArray();
        var strongIds = allowed
            .Select(update => Guid.Parse(update.Id))
            .ToHashSet();

        // Review-only updates still require a real ID match and known WUA
        // provenance; titles and unknown-origin or unrelated GUIDs never
        // become per-device evidence.
        var review = (reviewOnly ?? [])
            .Where(update =>
                WindowsDriverCandidateMerge.IsKnownWuaGuid(update) &&
                !QuickUpdatePolicy.IsEligible(update) &&
                DriverUpdateMatcher.MatchStrength(device, update) > 0)
            .Where(update => strongIds.Add(Guid.Parse(update.Id)))
            .ToArray();

        // Generic vendor/model or revisionless OEM SUBSYS matches are WEAK
        // evidence, separate from actual Windows Compatible IDs. Never
        // promote them to the ordinary per-device installation collection.
        var model = (modelOnlyReview ?? [])
            .Where(update =>
                WindowsDriverCandidateMerge.IsKnownWuaGuid(update) &&
                WuaGenericModelCorrelation.IsWeakModelOnlyMatch(
                    device, update))
            .Where(update => strongIds.Add(Guid.Parse(update.Id)))
            .Take(WuaGenericModelCorrelation.MaximumReviewHintsPerDevice)
            .ToArray();

        return new Result(allowed, review, model);
    }
}
