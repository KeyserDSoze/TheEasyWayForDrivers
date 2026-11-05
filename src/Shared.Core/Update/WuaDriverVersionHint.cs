using System.Text.RegularExpressions;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Extracts a *display-only hint* from the human-readable Windows Update
/// title. WUA exposes no strongly-typed comparable driver version for these
/// entries. A title hint is never used for version ordering or installation.
/// </summary>
public static class WuaDriverVersionHint
{
    private static readonly Regex Parenthesized = new(
        @"\((?<v>\d{1,6}(?:\.\d{1,6}){2,4})\)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex DashSeparated = new(
        @"\s-\s(?<v>\d{1,6}(?:\.\d{1,6}){2,4})\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static string? FromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 350)
            return null;

        var match = Parenthesized.Match(title);
        if (!match.Success)
            match = DashSeparated.Match(title);
        if (!match.Success)
            return null;

        var value = match.Groups["v"].Value;
        var parts = value.Split('.');
        if (parts.Length is < 3 or > 5)
            return null;

        // A trailing release date (e.g. "2026.11.05") is not evidence
        // of a version. If uncertain, do not show a version hint.
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out var year) &&
            year is >= 1990 and <= 2100 &&
            int.TryParse(parts[1], out var month) &&
            month is >= 1 and <= 12 &&
            int.TryParse(parts[2], out var day) &&
            day is >= 1 and <= 31)
            return null;

        return value;
    }
}
