using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// #479: canonical, scope-aware selection of ordinary WUA GUIDs. The
/// complete set of completed-source WUA records has veto authority:
/// a selected/filtered row cannot hide BrowseOnly or firmware evidence
/// for the SAME GUID from a different source or representation.
/// This is a Desktop permission boundary, not a guarantee that the
/// privileged Windows service will still find/apply that update.
/// </summary>
public static class CompactInstallActionPolicy
{
    public static IReadOnlyList<string> SelectIds(
        IEnumerable<DriverUpdateInfo> completeConfirmedRows,
        IEnumerable<DriverUpdateInfo> requestedRows)
    {
        ArgumentNullException.ThrowIfNull(completeConfirmedRows);
        ArgumentNullException.ThrowIfNull(requestedRows);
        var authorized = QuickUpdatePolicy.AllOrdinaryIds(
            completeConfirmedRows).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<Guid>();
        var ids = new List<string>();
        foreach (var update in requestedRows)
        {
            // Only individually eligible WUA rows can ask for a GUID,
            // even when another genuine WUA pass authorizes it.
            if (update is null || !QuickUpdatePolicy.IsEligible(update))
                continue;
            var id = Guid.Parse(update.Id);
            if (authorized.Contains(id.ToString("D")) && seen.Add(id))
                ids.Add(id.ToString("D"));
        }

        return ids;
    }

    /// <summary>
    /// Recheck the whole allowed GUID set after user confirmation. D, B,
    /// N and P GUID formats are equivalent; send ONLY D-format to IPC.
    /// Duplicate equivalent IDs and unknown/non-ordinary IDs fail closed.
    /// </summary>
    public static bool TryAuthorizeRequested(
        IEnumerable<string> authorizedCanonicalIds,
        IReadOnlyCollection<string> requestedIds,
        out IReadOnlyList<string> canonicalIds)
    {
        ArgumentNullException.ThrowIfNull(authorizedCanonicalIds);
        ArgumentNullException.ThrowIfNull(requestedIds);
        canonicalIds = [];
        if (requestedIds.Count == 0)
            return false;

        var allowed = authorizedCanonicalIds
            .Where(id => Guid.TryParse(id, out _))
            .Select(Guid.Parse)
            .ToHashSet();
        var seen = new HashSet<Guid>();
        var result = new List<string>(requestedIds.Count);
        foreach (var raw in requestedIds)
        {
            if (!Guid.TryParse(raw, out var id) ||
                !allowed.Contains(id) ||
                !seen.Add(id))
                return false;
            result.Add(id.ToString("D"));
        }
        canonicalIds = result;
        return true;
    }
}
