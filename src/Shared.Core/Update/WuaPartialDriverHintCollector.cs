using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// A title returned by an INCOMPLETE Windows Update Agent driver search.
/// It is deliberately a different type from DriverUpdateInfo. It is not an
/// offer to install, a verified PnP match or a manufacturer recommendation.
/// </summary>
public sealed record WuaPartialDriverHint(
    string UpdateId, string Title, string SearchSource)
{
    public bool IsInstallable => false;
    public bool IsVerified => false;
    public string Detail => SearchSource +
        " · ricerca WUA parziale (avvisi o errori); " +
        "solo indizio non verificato, NON installabile.";
}

public static class WuaPartialDriverHintCollector
{
    public const int MaximumHints = 32;
    public const int MaximumRawResultsPerPass = 4000;
    public const int MaximumStagedHints = 6 * MaximumRawResultsPerPass;

    private static readonly HashSet<string> AllowedSources =
        WindowsDriverSearchPlan.Create(DriverSearchMode.Comprehensive, true)
            .Select(pass => pass.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Retains only public, display-safe titles from recognized Type=Driver
    /// WUA passes. UpdateId is used only for de-duplication, never exposed in
    /// a URL, UI detail, issue report or install request.
    /// </summary>
    public static WuaPartialDriverHint? TryCreate(
        string? id, string? title, string source)
    {
        if (!AllowedSources.Contains(source) ||
            !Guid.TryParse(id, out var parsedId) ||
            string.IsNullOrWhiteSpace(title) ||
            title.Length > 200 ||
            title.Any(c => char.IsControl(c) ||
                c is '\\' or '/' or ':' or
                '\u202A' or '\u202B' or '\u202C' or '\u202D' or
                '\u202E' or '\u2066' or '\u2067' or '\u2068' or
                '\u2069'))
            return null;

        // Identity is the GUID value, not D/B/N/P text from COM/IPC.
        return new WuaPartialDriverHint(parsedId.ToString("D"),
            title.Trim(), source);
    }

    /// <summary>Returns newly added hints; omitted is the count beyond
    /// display budget, with cross-pass GUID de-duplication.</summary>
    public static int Merge(
        IDictionary<string, WuaPartialDriverHint> destination,
        IEnumerable<WuaPartialDriverHint> hints,
        out int omitted,
        ISet<string>? previouslyOmittedIds = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(hints);
        omitted = 0;
        var added = 0;
        // Legacy caller-supplied dictionaries/omitted sets may use B/N/P
        // GUID encodings. Parse the values, not string representations.
        var known = destination.Keys
            .Where(key => Guid.TryParse(key, out _))
            .Select(Guid.Parse).ToHashSet();
        var omittedGuids = previouslyOmittedIds?
            .Where(key => Guid.TryParse(key, out _))
            .Select(Guid.Parse).ToHashSet();
        var seen = new HashSet<Guid>();
        foreach (var hint in hints)
        {
            if (hint is null)
                continue;
            var safe = TryCreate(hint.UpdateId, hint.Title,
                hint.SearchSource);
            if (safe is null)
                continue;
            var guid = Guid.Parse(safe.UpdateId);
            if (!seen.Add(guid) || known.Contains(guid) ||
                omittedGuids?.Contains(guid) == true)
                continue;

            if (destination.Count >= MaximumHints)
            {
                if (omittedGuids is null || omittedGuids.Add(guid))
                {
                    previouslyOmittedIds?.Add(safe.UpdateId);
                    omitted++;
                }
                continue;
            }

            destination.Add(safe.UpdateId, safe);
            known.Add(guid);
            added++;
        }

        return added;
    }

    /// <summary>
    /// Retain safe titles from ALL fully enumerated partial Type=Driver
    /// WUA passes. This temporary staging is NOT installable. The merge
    /// is atomic on limits; a failed pass adds no partially selected rows.
    /// </summary>
    public static int MergeStaged(
        IDictionary<string, WuaPartialDriverHint> destination,
        IEnumerable<WuaPartialDriverHint> hints)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(hints);
        if (destination.Count > MaximumStagedHints)
            throw new InvalidDataException("Troppi indizi WUA parziali.");

        var pending = new List<WuaPartialDriverHint>();
        var known = destination.Keys
            .Where(id => Guid.TryParse(id, out _))
            .Select(Guid.Parse).ToHashSet();
        var seen = new HashSet<Guid>();
        var scanned = 0;
        foreach (var item in hints)
        {
            if (++scanned > MaximumRawResultsPerPass)
                throw new InvalidDataException("Pass WUA parziale oltre 4000 righe.");

            var valid = TryCreate(item.UpdateId, item.Title, item.SearchSource);
            if (valid is null)
                continue;
            var guid = Guid.Parse(valid.UpdateId);
            if (!seen.Add(guid) || known.Contains(guid))
                continue;

            if (destination.Count + pending.Count >= MaximumStagedHints)
                throw new InvalidDataException("Troppi indizi WUA parziali.");
            pending.Add(valid);
        }
        foreach (var item in pending)
            destination.Add(item.UpdateId, item);
        return pending.Count;
    }

    public sealed record FairSelection(
        IReadOnlyList<WuaPartialDriverHint> Hints, int Omitted);

    /// <summary>
    /// Confirmed GUIDs are removed BEFORE a global, vendor-fair and
    /// source-fair 32-row selection. No titles from partial WUA passes
    /// can become verified update candidates or installation requests.
    /// </summary>
    public static FairSelection SelectFair(
        IReadOnlyList<WuaPartialDriverHint> staged,
        IEnumerable<string>? confirmedUpdateIds = null)
    {
        ArgumentNullException.ThrowIfNull(staged);
        if (staged.Count > MaximumStagedHints)
            throw new InvalidDataException("Troppi indizi WUA parziali.");

        var confirmed = (confirmedUpdateIds ?? [])
            .Where(id => Guid.TryParse(id, out _))
            .Select(Guid.Parse).ToHashSet();
        var unique = staged
            .Where(item => item is not null)
            .Select(item => TryCreate(item.UpdateId, item.Title,
                item.SearchSource))
            .Where(item => item is not null &&
                !confirmed.Contains(Guid.Parse(item.UpdateId)))
            .Select(item => item!)
            .DistinctBy(item => item.UpdateId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unique.Length <= MaximumHints)
            return new(unique, 0);

        // Round-robin sources within each vendor, then vendors globally.
        // An early Intel-only pass cannot consume all later NVIDIA/Killer.
        var groups = unique.GroupBy(item => VendorGroup(item.Title))
            .Select(vendor =>
            {
                var sources = vendor.GroupBy(item => item.SearchSource)
                    .Select(source => source.ToArray()).ToArray();
                var interleaved = new List<WuaPartialDriverHint>();
                for (var depth = 0; depth < sources.Max(s => s.Length); depth++)
                    foreach (var source in sources)
                        if (depth < source.Length)
                            interleaved.Add(source[depth]);
                return interleaved;
            }).ToArray();

        var chosen = new List<WuaPartialDriverHint>(MaximumHints);
        for (var depth = 0; chosen.Count < MaximumHints; depth++)
        {
            var any = false;
            foreach (var group in groups)
            {
                if (depth >= group.Count)
                    continue;
                any = true;
                chosen.Add(group[depth]);
                if (chosen.Count == MaximumHints)
                    break;
            }
            if (!any)
                break;
        }
        return new(chosen, unique.Length - chosen.Count);
    }

    /// <summary>
    /// A Type=Driver WUA title from a partial enumeration is not proof of
    /// a completed query. Only the matching, unique, structurally valid
    /// Partial pass can authenticate an informational legacy IPC hint.
    /// Never use this gate as an install approval.
    /// </summary>
    public static bool IsVerifiablePartialPassStatus(
        DriverSearchPassStatus pass,
        IReadOnlyList<DriverSearchPassStatus> sources)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(sources);
        return AllowedSources.Contains(pass.Source) &&
            pass.Outcome == "Partial" &&
            pass.Candidates >= 0 && pass.NewUniqueCandidates >= 0 &&
            pass.NewUniqueCandidates <= pass.Candidates &&
            sources.Count(other => string.Equals(other.Source, pass.Source,
                StringComparison.OrdinalIgnoreCase)) == 1;
    }

    /// <summary>
    /// A legacy or externally supplied IPC report may contain unsafe titles,
    /// spoofed provenance, duplicated passes or updates also confirmed by
    /// an independent completed Type=Driver source. The same filtered list
    /// is used by Home and the anonymous QA report.
    /// </summary>
    public static IReadOnlyList<WuaPartialDriverHint> ConfirmedForReport(
        DriverSearchReport? report)
    {
        if (report is null ||
            report.PartialDriverReviewItems.Count > MaximumHints)
            return [];

        var partialSources = report.Sources
            .Where(pass => IsVerifiablePartialPassStatus(pass, report.Sources) &&
                // A partial query reporting 0 enumerated titles cannot
                // authenticate even one IPC hint. Preview rows for a
                // source can never exceed that source's result count.
                report.PartialDriverReviewItems.Count(hint =>
                    hint is not null &&
                    string.Equals(hint.SearchSource, pass.Source,
                        StringComparison.Ordinal)) <= pass.Candidates)
            .Select(pass => pass.Source)
            .ToHashSet(StringComparer.Ordinal);
        if (partialSources.Count == 0)
            return [];

        var confirmed = WindowsDriverCandidateMerge.ConfirmedForReport(report)
            .Where(update => Guid.TryParse(update.Id, out _))
            .Select(update => Guid.Parse(update.Id)).ToHashSet();

        var distinct = new HashSet<Guid>();
        var safe = new List<WuaPartialDriverHint>();
        foreach (var item in report.PartialDriverReviewItems)
        {
            if (item is null || !partialSources.Contains(item.SearchSource))
                continue;
            var hint = TryCreate(item.UpdateId, item.Title, item.SearchSource);
            if (hint is null)
                continue;
            var guid = Guid.Parse(hint.UpdateId);
            if (!confirmed.Contains(guid) && distinct.Add(guid))
                safe.Add(hint);
        }

        return SelectFair(safe).Hints;
    }

    // The omitted count has no per-source attribution in legacy IPC.
    // Reject overflow from malformed or over-budget snapshots rather than
    // reporting it as confirmed evidence.
    public static int VerifiedOmittedForReport(DriverSearchReport? report)
    {
        if (report is null || report.PartialDriverHintsOmitted < 0 ||
            report.PartialDriverHintsOmitted > MaximumStagedHints ||
            report.PartialDriverReviewItems.Count > MaximumHints ||
            report.Sources.Any(pass =>
                !WindowsDriverCandidateMerge.IsVerifiablePassStatus(
                    pass, report.Sources)) ||
            !report.Sources.Any(pass =>
                IsVerifiablePartialPassStatus(pass, report.Sources)) ||
            ConfirmedForReport(report).Count !=
                report.PartialDriverReviewItems.Count)
            return 0;

        return report.PartialDriverHintsOmitted;
    }

    private static string VendorGroup(string title)
    {
        // Purely display-fair grouping, never a PnP/driver determination.
        if (title.Contains("Killer", StringComparison.OrdinalIgnoreCase))
            return "Killer";
        if (title.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            return "NVIDIA";
        if (title.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
            return "Realtek";
        if (title.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase))
            return "Qualcomm";
        if (title.Contains("Broadcom", StringComparison.OrdinalIgnoreCase))
            return "Broadcom";
        if (title.Contains("MediaTek", StringComparison.OrdinalIgnoreCase))
            return "MediaTek";
        if (title.Contains("Advanced Micro Devices",
                StringComparison.OrdinalIgnoreCase) ||
            title.Contains("AMD ", StringComparison.OrdinalIgnoreCase))
            return "AMD";
        if (title.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return "Intel";
        return "Other";
    }

}
