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
            !Guid.TryParse(id, out _) ||
            string.IsNullOrWhiteSpace(title) ||
            title.Length > 200 ||
            title.Any(c => char.IsControl(c) ||
                c is '\\' or '/' or ':' or
                '\u202A' or '\u202B' or '\u202C' or '\u202D' or
                '\u202E' or '\u2066' or '\u2067' or '\u2068' or
                '\u2069'))
            return null;

        return new WuaPartialDriverHint(id!, title.Trim(), source);
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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hint in hints)
        {
            if (!seen.Add(hint.UpdateId) ||
                destination.ContainsKey(hint.UpdateId) ||
                previouslyOmittedIds?.Contains(hint.UpdateId) == true)
                continue;

            if (destination.Count >= MaximumHints)
            {
                if (previouslyOmittedIds is null ||
                    previouslyOmittedIds.Add(hint.UpdateId))
                    omitted++;
                continue;
            }

            destination.Add(hint.UpdateId, hint);
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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;
        foreach (var item in hints)
        {
            if (++scanned > MaximumRawResultsPerPass)
                throw new InvalidDataException("Pass WUA parziale oltre 4000 righe.");

            var valid = TryCreate(item.UpdateId, item.Title, item.SearchSource);
            if (valid is null || !seen.Add(valid.UpdateId) ||
                destination.ContainsKey(valid.UpdateId))
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

        var confirmed = confirmedUpdateIds?.ToHashSet(
            StringComparer.OrdinalIgnoreCase) ??
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = staged
            .Where(item => TryCreate(item.UpdateId, item.Title,
                item.SearchSource) is not null &&
                !confirmed.Contains(item.UpdateId))
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
