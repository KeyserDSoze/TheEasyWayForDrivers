using System.Text.RegularExpressions;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Exact model grammar for modern Windows SNDW and AMD ACP audio buses.
/// Class/type-only IDs are shared across devices and must NEVER create a
/// WUA/INF hardware match. This is read-only evidence, not install approval.
/// </summary>
public static class ModernAudioPnpMatchPolicy
{
    // The public SNDW model includes CLASS+MAN+PART; an optional complete
    // VER+UID pair is part of the original model identity. Never strip it
    // for exact matching or accept arbitrary suffixes/instance paths.
    private static readonly Regex Sndw = Pattern(
        @"\ASNDW\\CLASS_[0-9A-F]{2}&MAN_[0-9A-F]{4}&PART_[0-9A-F]{4}(?:&VER_[0-9A-F]{2}&UID_[0-9A-F]{2})?\z");

    // ACP hardware has a device TYPE and REV plus both VEN and DEV.
    // DEVTYPE-only or DEVTYPE+DEVREV is NOT a device model.
    private static readonly Regex Acp = Pattern(
        @"\AACP\\DEVTYPE_[0-9A-F]{4}&DEVREV_[0-9A-F]{4}&VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}\z");

    private static Regex Pattern(string pattern) =>
        new(pattern,
            RegexOptions.Compiled | RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

    public static bool IsSpecificHardwareId(string? id) =>
        id is not null &&
        id.Length is >= 24 and <= 160 &&
        id.Equals(id.Trim(), StringComparison.Ordinal) &&
        !id.Any(char.IsControl) &&
        (Sndw.IsMatch(id) || Acp.IsMatch(id));
}
