using System.Globalization;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Presentation-only Windows installed-driver version from WMI/IPC (#590).
/// A version is evidence ONLY of local metadata, never of OEM applicability
/// or a newer driver. Untrusted text and possible private identifiers are
/// replaced as a whole rather than shortened into a partial leak.
/// </summary>
public static class InstalledDriverVersionDisplay
{
    public const string Unavailable = "non disponibile";

    public static string Create(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return Unavailable;

        // Windows signed-driver versions contain 2-4 decimal 16-bit
        // components. Do not accept arbitrary strings, local paths,
        // device/serial IDs, invisible Unicode or unbounded numeric data.
        var text = version.Trim(' ');
        if (text.Length is < 3 or > 23)
            return Unavailable;

        var parts = text.Split('.');
        if (parts.Length is < 2 or > 4)
            return Unavailable;

        foreach (var part in parts)
        {
            if (part.Length is < 1 or > 5 ||
                !part.All(char.IsAsciiDigit) ||
                !ushort.TryParse(part, NumberStyles.None,
                    CultureInfo.InvariantCulture, out _))
                return Unavailable;
        }

        // Preserve the version as Windows reported it; never infer an
        // available, compatible or installable online update from it.
        return text;
    }
}
