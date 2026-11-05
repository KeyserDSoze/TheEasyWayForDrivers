using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Classifies diagnostics from actually attempted WUA search passes.
/// A service which has never been registered is not an attempted search and
/// must not poison the scan's success/failure status.
/// </summary>
public static class WuaSourceFailurePolicy
{
    public static IReadOnlyList<string> UnavailableSources(DriverSearchReport? report)
    {
        if (report is null)
            return [];

        return report.Sources
            .Where(source => !source.Succeeded &&
                source.Outcome != "NotRegistered")
            .Select(source => source.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
