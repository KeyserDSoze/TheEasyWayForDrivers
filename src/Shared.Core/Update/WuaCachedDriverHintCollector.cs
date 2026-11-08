namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// A title in Windows Update's LOCAL DATASTORE, not an online offer.
/// A previous scan may have cached a superseded or no-longer-applicable
/// driver. This distinct type can NEVER reach DriverUpdateInfo/Update All.
/// </summary>
public sealed record WuaCachedDriverHint(
    string UpdateId, string Title, bool? WasInstalled = null)
{
    public bool IsInstallable => false;
    public bool IsVerifiedAvailable => false;
    public string Detail =>
        "Archivio Windows locale (storico, scansione offline): " +
        (WasInstalled switch
        {
            true => "contrassegnato come già installato dalla cache; ",
            false => "contrassegnato come non installato dalla cache; ",
            _ => "stato installazione nella cache sconosciuto; "
        }) +
        "disponibilità attuale, compatibilità OEM e versione NON verificate. " +
        "Solo consultazione; non entra in Aggiorna tutto.";
}

public static class WuaCachedDriverHintCollector
{
    public const int MaximumRawResultsPerPass = 4000;
    public const int MaximumHints = 32;
    public const int MaximumTitleLength = 200;
    public const string Source = "Cache Windows locale · storico";

    public static WuaCachedDriverHint? TryCreate(
        string? id, string? title, bool? wasInstalled = null)
    {
        if (!Guid.TryParse(id, out _) ||
            string.IsNullOrWhiteSpace(title) ||
            title.Length > MaximumTitleLength ||
            title.Any(c => char.IsControl(c) ||
                c is '\\' or '/' or ':' or
                    '\u202A' or '\u202B' or '\u202C' or '\u202D' or
                    '\u202E' or '\u2066' or '\u2067' or '\u2068' or
                    '\u2069'))
            return null;

        return new WuaCachedDriverHint(id!, title.Trim(), wasInstalled);
    }

    /// <summary>
    /// Balance the 32-row maximum across both independently verified offline
    /// searches. If 4000 old uninstalled entries come first, an ordinary
    /// append would hide ALL installed-driver history. Interleave one row
    /// of each source until the cap; a short source cannot waste capacity.
    /// GUID dedup and online exclusions stay centralized in Merge.
    /// </summary>
    public static int MergeBalanced(
        IDictionary<string, WuaCachedDriverHint> destination,
        IReadOnlyList<WuaCachedDriverHint> notInstalled,
        IReadOnlyList<WuaCachedDriverHint> installed,
        IEnumerable<string> confirmedOnlineGuids,
        out int omitted)
    {
        ArgumentNullException.ThrowIfNull(notInstalled);
        ArgumentNullException.ThrowIfNull(installed);
        if (notInstalled.Count > MaximumRawResultsPerPass ||
            installed.Count > MaximumRawResultsPerPass)
            throw new InvalidDataException(
                "Elenco cache WUA oltre il limite dei record offline.");

        // A GUID may appear in two cached revisions with opposite states:
        // never claim "installed" or "not installed" for an ambiguous ID.
        // Keep a single review title with unknown state instead.
        var installedIds = installed.Select(h => h.UpdateId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notInstalledIds = notInstalled.Select(h => h.UpdateId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        WuaCachedDriverHint ConservativelyClassify(WuaCachedDriverHint hint) =>
            installedIds.Contains(hint.UpdateId) &&
            notInstalledIds.Contains(hint.UpdateId)
                ? hint with { WasInstalled = null }
                : hint;

        IEnumerable<WuaCachedDriverHint> Interleave()
        {
            for (var i = 0; i < Math.Max(notInstalled.Count, installed.Count); i++)
            {
                if (i < notInstalled.Count)
                    yield return ConservativelyClassify(notInstalled[i]);
                if (i < installed.Count)
                    yield return ConservativelyClassify(installed[i]);
            }
        }

        return Merge(destination, Interleave(), confirmedOnlineGuids,
            out omitted);
    }

    /// <summary>
    /// Count only unique cached GUIDs absent from all CONFIRMED online
    /// Type=Driver passes. A GUID omitted by the 32-row display cap is
    /// counted once, not repeatedly across duplicate cache entries.
    /// </summary>
    public static int Merge(
        IDictionary<string, WuaCachedDriverHint> destination,
        IEnumerable<WuaCachedDriverHint> candidates,
        IEnumerable<string> confirmedOnlineGuids,
        out int omitted)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(confirmedOnlineGuids);

        var confirmed = confirmedOnlineGuids.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        omitted = 0;
        var added = 0;
        foreach (var hint in candidates)
        {
            if (!seen.Add(hint.UpdateId) ||
                confirmed.Contains(hint.UpdateId) ||
                destination.ContainsKey(hint.UpdateId))
                continue;

            if (destination.Count >= MaximumHints)
            {
                omitted++;
                continue;
            }

            destination.Add(hint.UpdateId, hint);
            added++;
        }
        return added;
    }
}
