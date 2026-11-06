using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Optional read-only enrichment through IWindowsDriverUpdate4.
/// A WUA update may expose multiple IWindowsDriverUpdateEntry rows. Failure
/// to access the newer COM interface MUST NOT invalidate the update itself.
/// No Device Instance IDs, downloads, installation or WUA mutations.
/// </summary>
public static class WindowsDriverEntryIdReader
{
    public const int MaximumEntries = WuaDriverEntryLimits.MaximumAdditionalIds;
    private const int MaximumIdLength = WuaDriverEntryLimits.MaximumIdLength;

    public sealed record EntryIds(
        IReadOnlyList<string> AdditionalHardwareIds,
        bool Truncated);

    // Backwards-compatible helper for existing service tests and consumers.
    public static IReadOnlyList<string> ReadAdditionalIds(
        object update, string? primaryHardwareId,
        CancellationToken cancellationToken = default) =>
        ReadAdditionalIdsWithCoverage(
            update, primaryHardwareId, cancellationToken).AdditionalHardwareIds;

    public static EntryIds ReadAdditionalIdsWithCoverage(
        object update, string? primaryHardwareId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        cancellationToken.ThrowIfCancellationRequested();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(primaryHardwareId))
            seen.Add(primaryHardwareId.Trim());

        var additional = new List<string>();

        try
        {
            // This is IWindowsDriverUpdate4.WindowsDriverUpdateEntries;
            // older WUA COM implementations may not expose it.
            dynamic entries = ((dynamic)update).WindowsDriverUpdateEntries;
            if (entries is null)
                return new EntryIds([], true); // no evidence that all entries were read

            var count = (int)entries.Count;
            if (count < 0)
                throw new InvalidDataException("Numero di entry WUA non valido.");

            // A broken single COM entry must not erase VALID IDs read
            // before it or hide healthy entries later in the collection.
            // Never present the resulting set as exhaustive: one skipped
            // entry can hold the ID of a different Windows device.
            var incomplete = count > MaximumEntries;
            for (var i = 0; i < Math.Min(count, MaximumEntries); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    dynamic entry = entries.Item(i);
                    object? raw = entry.DriverHardwareID;
                    if (raw is not string id || !IsPnpId(id))
                    {
                        incomplete = true;
                        continue;
                    }

                    if (seen.Add(id))
                        additional.Add(id);
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException &&
                    exception is not OutOfMemoryException)
                {
                    // Only optional metadata for this entry is unavailable.
                    // Continue bounded enumeration with a visible incomplete
                    // flag; the already-confirmed WUA update is unchanged.
                    incomplete = true;
                }
            }

            return new EntryIds(additional, incomplete);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            exception is not OutOfMemoryException)
        {
            // The collection itself is unavailable or malformed. Keep
            // any individually validated IDs and never claim full coverage.
            return new EntryIds(additional, true);
        }
    }

    private static bool IsPnpId(string id)
    {
        if (id.Length is < 3 or > MaximumIdLength)
            return false;

        var separator = id.IndexOf('\\');
        return separator is > 0 and < 32 &&
            separator < id.Length - 1 &&
            separator == id.LastIndexOf('\\') &&
            !id.Contains("..", StringComparison.Ordinal) &&
            !id.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) ||
                c is '/' or ':' or '\u202A' or '\u202B' or '\u202C' or
                    '\u202D' or '\u202E' or '\u2066' or '\u2067' or
                    '\u2068' or '\u2069');
    }
}
