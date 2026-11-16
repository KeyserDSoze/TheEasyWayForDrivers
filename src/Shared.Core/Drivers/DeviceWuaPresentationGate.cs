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
        IReadOnlyList<DriverUpdateInfo>? modelOnlyReview = null,
        IReadOnlySet<Guid>? approvedSourceGuids = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(ordinary);

        // The same WUA GUID can appear in several completed passes, or
        // even in several presentation buckets in a legacy IPC snapshot.
        // Reuse Update All's canonical GUID veto across ALL three buckets:
        // a trusted BrowseOnly/Firmware representation blocks one-click
        // presentation even if another representation is ordinary.
        // Unknown/OEM origins cannot veto a genuine Windows Update GUID.
        var allRepresentations = ordinary
            .Concat(reviewOnly ?? [])
            .Concat(modelOnlyReview ?? [])
            .ToArray();
        var eligibleIds = QuickUpdatePolicy.AllOrdinaryIds(allRepresentations)
            .Select(Guid.Parse)
            .ToHashSet();

        // #481: the complete confirmed WUA report can contain a BrowseOnly
        // or Firmware veto that did not survive an IPC/presentation merge.
        // This extra allowlist comes from the whole report, never from a
        // per-device or filtered subset. Null preserves Core-only callers.
        bool AllowedByFullReport(DriverUpdateInfo update) =>
            approvedSourceGuids is null ||
            approvedSourceGuids.Contains(Guid.Parse(update.Id));

        // Only ordinary, confirmed-origin GUIDs with an exact PnP Hardware
        // or Compatible ID can enable a per-device update in the Desktop.
        var allowed = ordinary
            .Where(update => QuickUpdatePolicy.IsEligible(update) &&
                eligibleIds.Contains(Guid.Parse(update.Id)) &&
                AllowedByFullReport(update) &&
                DriverUpdateMatcher.MatchStrength(device, update) > 0)
            .DistinctBy(update => Guid.Parse(update.Id))
            .ToArray();
        var strongIds = allowed
            .Select(update => Guid.Parse(update.Id))
            .ToHashSet();

        // Review-only entries are always real PnP ID matches from known WUA
        // origins. If a trusted same-GUID veto blocks a formerly ordinary
        // record, retain a diagnostic review row without the install action.
        // Prefer explicit Hidden/BrowseOnly/Firmware evidence over an
        // ordinary title, regardless of its original bucket/order.
        var review = (reviewOnly ?? []).Concat(ordinary)
            .Where(update =>
                WindowsDriverCandidateMerge.IsKnownWuaGuid(update) &&
                (!QuickUpdatePolicy.IsEligible(update) ||
                 !eligibleIds.Contains(Guid.Parse(update.Id)) ||
                 !AllowedByFullReport(update)) &&
                DriverUpdateMatcher.MatchStrength(device, update) > 0)
            .GroupBy(update => Guid.Parse(update.Id))
            .Select(group => group.FirstOrDefault(update =>
                update.IsHidden ||
                update.IsOptional ||
                QuickUpdatePolicy.IsFirmwareOrBios(
                    update.Title, update.DriverClass)) ?? group.First())
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
