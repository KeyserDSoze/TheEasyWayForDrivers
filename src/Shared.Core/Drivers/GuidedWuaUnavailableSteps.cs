using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// A WUA-only search replaces only the previous Windows source outcomes.
/// OEM catalog, inventory and companion failures are independent and must
/// remain until their own checks run. Never copy raw IPC source labels.
/// </summary>
public static class GuidedWuaUnavailableSteps
{
    public const string SearchStep = "ricerca Windows Update";
    private const string SourcePrefix = "Windows Update: ";

    public static IReadOnlyCollection<string> WithLatestReport(
        IEnumerable<string> previousUnavailableSteps,
        DriverSearchReport? report)
    {
        ArgumentNullException.ThrowIfNull(previousUnavailableSteps);

        var updated = previousUnavailableSteps
            .Where(step => step is not null &&
                !string.Equals(step, SearchStep, StringComparison.Ordinal) &&
                !step.StartsWith(SourcePrefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report))
            updated.Add(SearchStep);

        // WuaSourceFailurePolicy returns only canonical compiled-in source
        // names or a fixed unknown-source label, never the raw IPC input.
        foreach (var source in WuaSourceFailurePolicy.UnavailableSources(report))
            updated.Add(SourcePrefix + source);

        return updated.Distinct(StringComparer.Ordinal).ToArray();
    }
}
