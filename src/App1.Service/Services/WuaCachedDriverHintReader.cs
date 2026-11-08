using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Extracts non-installable titles from one COMPLETE offline WUA scan.
/// The caller MUST require a successful, warning-free ISearchResult first.
/// Failed Item/Identity/Count getters reject the entire collection, not
/// a partial "available" driver list. Optional Title failures skip one row.
/// </summary>
public static class WuaCachedDriverHintReader
{
    public sealed record Result(
        IReadOnlyList<WuaCachedDriverHint> Hints,
        int UnreadableTitles);

    /// <summary>Explicit offline read-only WUA query configuration.
    /// Leaves ServerSelection and ServiceID untouched to respect Windows
    /// Update/WSUS policy. No registration, WUA installation or download.
    /// </summary>
    public static void ConfigureOffline(object searcher)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        dynamic target = searcher;
        target.Online = false;
        target.IncludePotentiallySupersededUpdates = true;
    }

    public static Result ReadAll(
        object updates, int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (count is < 0 or > WuaCachedDriverHintCollector.MaximumRawResultsPerPass)
            throw new InvalidDataException(
                "Risultati cache driver WUA oltre il limite verificabile.");

        var result = new List<WuaCachedDriverHint>();
        var unreadable = 0;
        dynamic collection = updates;
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Mandatory: structural collection and GUID identity. No hint
            // can escape before every COM Item and identity is enumerated.
            dynamic update = collection.Item(i);
            dynamic identity = update.Identity;
            string? guid = (string?)identity.UpdateID;
            if (!Guid.TryParse(guid, out _))
                throw new InvalidDataException(
                    "Identità GUID cache WUA non verificabile.");
            var title = WuaSourceMetadataReader.ReadOptionalSoftwareTitle(
                (object)update);
            if (title.Unreadable)
                unreadable++;
            var hint = WuaCachedDriverHintCollector.TryCreate(
                guid, title.Text);
            if (hint is not null)
                result.Add(hint);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new Result(result, unreadable);
    }
}
