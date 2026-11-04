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
    IReadOnlyList<DriverSearchPassStatus> Sources)
{
    // A service which is not registered was not queried, but is not a
    // failed scan of an otherwise completed set of configured sources.
    public bool IsPartial => Sources.Count == 0 ||
        Sources.Any(source => !source.Succeeded && source.Outcome != "NotRegistered");
    public bool AllFailed => !Sources.Any(source => source.Succeeded);
}
