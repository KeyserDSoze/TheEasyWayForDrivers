using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Provides safe labels for WUA sources that failed or whose provenance
/// cannot be verified. The returned strings are displayed directly in
/// the Home error list: never return a name copied from untrusted IPC.
/// </summary>
public static class WuaSourceFailurePolicy
{
    public const string UnknownSourceLabel =
        "Fonte Windows non riconosciuta";

    private static readonly HashSet<string> KnownNames =
        WindowsDriverSearchPlan.Create(
                DriverSearchMode.Comprehensive, microsoftUpdateRegistered: true)
            .Select(pass => pass.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> UnavailableSources(DriverSearchReport? report)
    {
        if (report is null)
            return [];

        var unavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var status in report.Sources)
        {
            // The same gate determines whether a WUA source may supply
            // confirmed GUID evidence, be displayed as "Completed" in
            // Home or be labeled "interrogata" in copied coverage.
            // A duplicated Completed, unknown source, impossible counter
            // or invalid NotRegistered is NOT a completed verified pass.
            var verified = WindowsDriverCandidateMerge.IsVerifiablePassStatus(
                status, report.Sources);
            if (verified && (status.Succeeded ||
                             status.Outcome == "NotRegistered"))
                continue;

            // Source is an IPC field: never echo a WSUS hostname,
            // device identifier, private path or arbitrary source label.
            var safe = status.Source is { } name &&
                KnownNames.TryGetValue(name, out var canonical)
                    ? canonical
                    : UnknownSourceLabel;
            unavailable.Add(safe);
        }

        return unavailable.ToArray();
    }
}
