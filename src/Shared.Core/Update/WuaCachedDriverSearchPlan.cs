namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Explicit read-only historical Windows Update DATASTORE searches.
/// Query both installed and not-installed cached driver records: neither
/// category represents a currently available ONLINE update.
/// </summary>
public static class WuaCachedDriverSearchPlan
{
    public sealed record Pass(string Name, string Criteria, bool WasInstalled);

    public static IReadOnlyList<Pass> Create() =>
    [
        new("Cache Windows locale · non installati",
            "IsInstalled=0 and Type='Driver'", false),
        new("Cache Windows locale · già installati",
            "IsInstalled=1 and Type='Driver'", true)
    ];
}
