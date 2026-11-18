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
            // #554: use exactly the same source-local gate as confirmed
            // GUID evidence and WuaPassStatusPresentation. Checking only
            // the standalone pass outcome/counters misses a Completed COM
            // pass whose attributed rows EXCEED the raw candidates count.
            // Such a source is not available/verified even if the label
            // says Completed. Never print its untrusted IPC detail.
            var verified = WindowsDriverCandidateMerge.IsVerifiablePassForReport(
                status, report);
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
