namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// A title in Windows Update's LOCAL DATASTORE, not an online offer.
/// A previous scan may have cached a superseded or no-longer-applicable
/// driver. This distinct type can NEVER reach DriverUpdateInfo/Update All.
/// </summary>
public sealed record WuaCachedDriverHint(string UpdateId, string Title)
{
    public bool IsInstallable => false;
    public bool IsVerifiedAvailable => false;
    public string Detail =>
        "Archivio Windows locale (storico, scansione offline): " +
        "disponibilità, compatibilità OEM e versione NON verificate. " +
        "Solo consultazione; non entra in Aggiorna tutto.";
}

public static class WuaCachedDriverHintCollector
{
    public const int MaximumRawResultsPerPass = 4000;
    public const int MaximumHints = 32;
    public const int MaximumTitleLength = 200;
    public const string Source = "Cache Windows locale · storico";

    public static WuaCachedDriverHint? TryCreate(string? id, string? title)
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

        return new WuaCachedDriverHint(id!, title.Trim());
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
