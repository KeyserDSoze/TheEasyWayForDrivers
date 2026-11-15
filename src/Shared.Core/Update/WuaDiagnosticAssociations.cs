using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Display-only correlation of confirmed Type=Driver WUA GUIDs to PnP IDs
/// across sources. Does not expose installable update metadata or actions.
/// </summary>
public sealed record WuaDiagnosticAssociation(
    string UpdateId,
    string? PrimaryHardwareId,
    IReadOnlyList<string>? AdditionalHardwareIds,
    // #467: this is evidence AFTER GUID merging and legacy IPC PnP
    // sanitization, not the source record's pre-merge transport flag.
    // A rejected malformed ID means a zero-match Home card must not
    // imply all WUA per-device associations were inspected.
    bool AdditionalHardwareIdsIncomplete = false)
{
    public bool Matches(DriverInfo device) =>
        DriverUpdateMatcher.MatchStrength(
            device, PrimaryHardwareId, AdditionalHardwareIds) > 0;
}

public static class WuaDiagnosticAssociations
{
    public static IReadOnlyList<WuaDiagnosticAssociation> Create(
        IEnumerable<DriverUpdateInfo> confirmedDriverUpdates)
    {
        ArgumentNullException.ThrowIfNull(confirmedDriverUpdates);

        // DistinctBy(Id) lost later Windows Update source Hardware IDs for
        // the SAME GUID. Reuse the bounded source merge ONLY for diagnostic
        // association; never grant installation permission on this evidence.
        var byGuid = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(byGuid,
            confirmedDriverUpdates.Where(
                WindowsDriverCandidateMerge.IsKnownWuaGuid));
        return byGuid.Values
            .Select(update => new WuaDiagnosticAssociation(
                update.Id, update.HardwareId, update.AdditionalHardwareIds,
                update.AdditionalHardwareIdsIncomplete))
            .ToArray();
    }
}
