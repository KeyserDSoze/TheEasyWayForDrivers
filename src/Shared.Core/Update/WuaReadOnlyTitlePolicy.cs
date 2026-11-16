using System.Globalization;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Only for read-only WUA Type=Driver partial hints and local cache titles.
/// These are untrusted display strings, even when a source was queried.
/// A rejected title is omitted; it is NEVER transformed into installable
/// DriverUpdateInfo or echoed to an export. Keep this separate from WUA
/// update identity and hardware matching policies.
/// </summary>
internal static class WuaReadOnlyTitlePolicy
{
    public const int MaximumLength = 200;

    private static readonly string[] PrivateMarkers =
    [
        "VEN_", "VID_", "DEV_", "PID_", "SUBSYS_", "REV_",
        "FUNC_", "MI_", "INSTANCEID", "SERIALNUMBER", "SERIAL_",
        "PRIVATE_", "SECRET_", "ORDER_"
    ];

    public static bool IsSafe(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) ||
            title.Length > MaximumLength ||
            PrivateMarkers.Any(marker =>
                title.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Keep an ordinary commercial title such as Intel(R) Killer Wi-Fi
        // or an accented name. Untrusted WUA/IPC titles must not render
        // an address, path, hidden Unicode direction/format marker, or
        // non-ASCII whitespace that can disguise a private identifier.
        return !title.Any(ch =>
            char.IsControl(ch) || char.IsSurrogate(ch) ||
            ch is '\\' or '/' or ':' or '@' or '\uFFFD' ||
            (char.IsWhiteSpace(ch) && ch != ' ') ||
            char.GetUnicodeCategory(ch) is
                UnicodeCategory.Format or
                UnicodeCategory.LineSeparator or
                UnicodeCategory.ParagraphSeparator);
    }
}
