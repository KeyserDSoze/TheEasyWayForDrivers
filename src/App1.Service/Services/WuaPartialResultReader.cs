using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Reads display-safe titles from a bounded, already classified INCOMPLETE
/// WUA Type=Driver result. Enumerates EVERY COM item before publishing a
/// single hint. No hardware IDs, update packages or COM warning messages
/// leave this reader. Never used by installation or the verified WUA list.
/// </summary>
public static class WuaPartialResultReader
{
    public static IReadOnlyList<WuaPartialDriverHint> ReadAll(
        object updates, int count, string searchSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (count is < 0 or > WuaPartialDriverHintCollector.MaximumRawResultsPerPass)
            throw new InvalidDataException(
                "Elenco WUA oltre il limite: ricerca non verificata.");

        // The returned list is committed only after an entire successful
        // enumeration. A mid-pass COM failure discards ALL staged titles.
        var staged = new List<WuaPartialDriverHint>();
        dynamic collection = updates;
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            dynamic update = collection.Item(index);
            dynamic identity = update.Identity;
            string? id = identity.UpdateID as string;
            string? title = update.Title as string;
            var hint = WuaPartialDriverHintCollector.TryCreate(
                id, title, searchSource);
            if (hint is not null)
                staged.Add(hint);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return staged;
    }
}
