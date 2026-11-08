namespace TheEasyWayForDrivers.Core.Models;

/// <summary>
/// Evidence of which Windows Update sources were actually queried.
/// A source count is the number of candidates RETURNED by that pass before de-duplication.
/// OEM handoffs are NOT part of these search passes.
/// </summary>
public sealed record DriverSearchPassStatus(
    string Source,
    string Outcome,
    int Candidates,
    string Detail,
    int NewUniqueCandidates = 0)
{
    public bool Succeeded => Outcome == "Completed";
}

public sealed record DriverSearchReport(
    IReadOnlyList<DriverUpdateInfo> Updates,
    IReadOnlyList<DriverSearchPassStatus> Sources,
    IReadOnlyList<TheEasyWayForDrivers.Core.Update.WuaDriverRelatedSoftware>? RelatedSoftware = null,
    IReadOnlyList<DriverSearchPassStatus>? RelatedSoftwareSources = null,
    int RelatedSoftwareOmitted = 0,
    // Incomplete WUA Type=Driver searches: informational ONLY. Never
    // contribute to Updates, driver matches, bulk install or notifications.
    IReadOnlyList<TheEasyWayForDrivers.Core.Update.WuaPartialDriverHint>? PartialDriverHints = null,
    int PartialDriverHintsOmitted = 0,
    // Separate review-only titles from WUA's local offline DATASTORE.
    // Cached entries may be stale and are NEVER DriverUpdateInfo.
    IReadOnlyList<TheEasyWayForDrivers.Core.Update.WuaCachedDriverHint>? CachedDriverHints = null,
    IReadOnlyList<DriverSearchPassStatus>? CachedDriverSources = null,
    int CachedDriverHintsOmitted = 0)
{
    public IReadOnlyList<TheEasyWayForDrivers.Core.Update.WuaDriverRelatedSoftware>
        SoftwareReviewItems => RelatedSoftware ?? [];
    public IReadOnlyList<TheEasyWayForDrivers.Core.Update.WuaPartialDriverHint>
        PartialDriverReviewItems => PartialDriverHints ?? [];
    public IReadOnlyList<TheEasyWayForDrivers.Core.Update.WuaCachedDriverHint>
        CachedDriverReviewItems => CachedDriverHints ?? [];
    // Cache discovery is OPTIONAL and cannot make a healthy Type=Driver
    // online scan "partial" or disable ordinary WUA installation.
    public bool CachedDriverSearchFailed => CachedDriverSources is { Count: > 0 } &&
        CachedDriverSources.Any(source => !source.Succeeded);

    // Type=Software is an independent, informational search. Failed optional
    // software passes are surfaced in diagnostics but do not invalidate a
    // successful Type=Driver scan or disable its WUA-only installation flow.
    public bool SoftwareSearchIsPartial => RelatedSoftwareSources is { Count: > 0 } &&
        RelatedSoftwareSources.Any(source => !source.Succeeded &&
            source.Outcome != "NotRegistered");

    // A WUA service that is not registered was never queried. Only failed
    // Type=Driver searches influence bulk-install availability.
    public bool IsPartial => Sources.Count == 0 ||
        Sources.Any(source => !source.Succeeded &&
            source.Outcome != "NotRegistered");
    public bool AllFailed => !Sources.Any(source => source.Succeeded);
}
