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

    // Only the two documented read-only queries of the LOCAL WUA
    // datastore may authenticate historical hints from IPC snapshots.
    // The older summary label "storico" is not itself an executed pass.
    private static readonly string UninstalledSource =
        WuaCachedDriverSearchPlan.Create()[0].Name;
    private static readonly string InstalledSource =
        WuaCachedDriverSearchPlan.Create()[1].Name;

    public static bool IsVerifiableCachePassStatus(
        TheEasyWayForDrivers.Core.Models.DriverSearchPassStatus pass,
        IReadOnlyList<TheEasyWayForDrivers.Core.Models.DriverSearchPassStatus> sources)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(sources);
        return (pass.Source == UninstalledSource || pass.Source == InstalledSource) &&
            pass.Outcome != "NotRegistered" &&
            (!pass.Succeeded ||
             (pass.Candidates >= 0 && pass.NewUniqueCandidates >= 0 &&
              pass.NewUniqueCandidates <= pass.Candidates)) &&
            sources.Count(other => string.Equals(other.Source, pass.Source,
                StringComparison.OrdinalIgnoreCase)) == 1;
    }

    public static bool HasUnverifiableCachePassStatuses(
        IReadOnlyList<TheEasyWayForDrivers.Core.Models.DriverSearchPassStatus> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        // A full Comprehensive-mode cache search always has exactly
        // two status rows. Unknown, duplicated or missing rows cannot
        // support a claim of complete historical coverage.
        return sources.Count != 2 || sources.Any(pass =>
            !IsVerifiableCachePassStatus(pass, sources));
    }

    public static bool HasCompletedKnownCachePass(
        TheEasyWayForDrivers.Core.Models.DriverSearchReport? report) =>
        report?.CachedDriverSources is { } sources &&
        sources.Any(pass => pass.Succeeded &&
            IsVerifiableCachePassStatus(pass, sources));

    // The omitted total has no per-pass attribution. On a partial or
    // malformed legacy snapshot it is not safe to call it a verified
    // count of historical titles outside the 32-row preview.
    public static int VerifiedOmittedForReport(
        TheEasyWayForDrivers.Core.Models.DriverSearchReport? report) =>
        report?.CachedDriverSources is { Count: 2 } sources &&
        sources.All(pass => pass.Succeeded &&
            IsVerifiableCachePassStatus(pass, sources))
            ? Math.Max(0, report.CachedDriverHintsOmitted)
            : 0;

    /// <summary>
    /// Validate legacy / externally supplied IPC hints at the last
    /// read-only display boundary. Individual cached hints have no pass
    /// name, so WasInstalled maps to the verified offline query. A null
    /// state can only be genuine cross-pass ambiguity if BOTH queries
    /// completed. Nothing here becomes an online candidate or update.
    /// </summary>
    public static IReadOnlyList<WuaCachedDriverHint> ConfirmedForReport(
        TheEasyWayForDrivers.Core.Models.DriverSearchReport? report)
    {
        if (report?.CachedDriverSources is not { } sources ||
            report.CachedDriverReviewItems.Count > MaximumHints)
            return [];

        var verifiedUninstalled = sources.Any(pass =>
            pass.Source == UninstalledSource && pass.Succeeded &&
            IsVerifiableCachePassStatus(pass, sources));
        var verifiedInstalled = sources.Any(pass =>
            pass.Source == InstalledSource && pass.Succeeded &&
            IsVerifiableCachePassStatus(pass, sources));

        if (!verifiedUninstalled && !verifiedInstalled)
            return [];

        var seen = new HashSet<Guid>();
        var safe = new List<WuaCachedDriverHint>();
        foreach (var hint in report.CachedDriverReviewItems)
        {
            if (hint is null ||
                (hint.WasInstalled is true && !verifiedInstalled) ||
                (hint.WasInstalled is false && !verifiedUninstalled) ||
                (hint.WasInstalled is null &&
                 !(verifiedInstalled && verifiedUninstalled)))
                continue;

            var checkedHint = TryCreate(hint.UpdateId, hint.Title,
                hint.WasInstalled);
            if (checkedHint is null)
                continue;
            var guid = Guid.Parse(checkedHint.UpdateId);
            if (seen.Add(guid))
                safe.Add(checkedHint with { UpdateId = guid.ToString("D") });
        }
        return safe;
    }

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
