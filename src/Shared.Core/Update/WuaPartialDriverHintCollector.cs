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
                c is '\\' or '/' or ':' ||
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
}
