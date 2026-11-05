namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Optional read-only enrichment through IWindowsDriverUpdate4.
/// A WUA update may expose multiple IWindowsDriverUpdateEntry rows. Failure
/// to access the newer COM interface MUST NOT invalidate the update itself.
/// No Device Instance IDs, downloads, installation or WUA mutations.
/// </summary>
public static class WindowsDriverEntryIdReader
{
    public const int MaximumEntries = 128;
    private const int MaximumIdLength = 512;

    public static IReadOnlyList<string> ReadAdditionalIds(
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
                return additional;

            var count = (int)entries.Count;
            for (var i = 0; i < Math.Min(Math.Max(0, count), MaximumEntries); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dynamic entry = entries.Item(i);
                object? raw = entry.DriverHardwareID;
                if (raw is not string id)
                    continue;

                id = id.Trim();
                if (!IsPnpId(id) || !seen.Add(id))
                    continue;

                additional.Add(id);
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            exception is not OutOfMemoryException)
        {
            // All-or-nothing optional metadata: never claim an incomplete
            // COM enumeration is the complete list of device associations.
            return [];
        }

        return additional;
    }

    private static bool IsPnpId(string id)
    {
        if (id.Length is < 3 or > MaximumIdLength)
            return false;

        var separator = id.IndexOf('\\');
        return separator is > 0 and < 32 &&
            separator < id.Length - 1 &&
            !id.Any(c => char.IsControl(c) || c is '/' or ':');
    }
}
