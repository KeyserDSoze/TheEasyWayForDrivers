namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Requires an exact, bounded set of confirmed WUA GUIDs before downloading.
/// A partially refreshed UpdateCollection cannot silently become "Update All".
/// </summary>
public static class WuaBatchSelectionGate
{
    public static bool IsValidRequest(IReadOnlySet<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Count is >= 1 and <= WuaBatchResultVerifier.MaxBatchUpdates &&
            ids.All(id => Guid.TryParse(id, out _));
    }

    public static bool IsComplete(
        IReadOnlySet<string> requested,
        IReadOnlySet<string> committed,
        int selectedCollectionCount)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(committed);

        return IsValidRequest(requested) &&
            selectedCollectionCount == requested.Count &&
            committed.Count == requested.Count &&
            requested.All(committed.Contains);
    }
}
