using System.Text;
using System.Text.RegularExpressions;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Conservative, local-only evidence from Windows PnPUtil /enum-devices /drivers.
/// No driver package is loaded, staged, installed, ranked by OmegaDrive or
/// classified as an update. PnPUtil's text is localized and is NOT parsed for
/// machine-actionable rank or update status.
/// </summary>
public static class WindowsDriverStoreEvidence
{
    public const int MaxOutputCharacters = 48 * 1024;
    public const int MaxDeviceIdCharacters = 512;

    // Native PnPUtil text may reference secondary hardware not included
    // in this device's WMI/SetupAPI inventory. The report is <= 48 KiB.
    // A PnP identifier can have a suffix with spaces or private adjacent
    // text. Matching only the contiguous token leaves the rest exposed:
    // redact the ENTIRE matching line before exact-ID replacements.
    // An enumerator can start with a digit (e.g. 1394), contain '-' or
    // have a one-character model segment. These IDs must be protected too.
    private static readonly Regex ExtraPnpIdentity = new(
        @"(?<![A-Za-z0-9_])[A-Za-z0-9][A-Za-z0-9_-]{1,31}\\[A-Za-z0-9_*&%{}#+.\-]+(?:\\[A-Za-z0-9_*&%{}#+.\-]+)*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));

    // Local paths can contain names, order references and spaces. Match
    // drive-letter paths with either separator and UNC-like prefixes.
    // Redact the entire line, including arbitrary text BEFORE the path.
    private static readonly Regex LocalPath = new(
        @"(?<![A-Za-z0-9])(?:[A-Za-z]:[\\/]|\\\\|//)[^\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(500));

    private static readonly Regex WebAddress = new(
        @"https?://[^\r\n]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));

    // PnPUtil exit code 0 plus headings or a Rank line does NOT establish
    // that a driver package was enumerated. After whole-line redaction, find
    // one independent ASCII INF basename without a regex timeout (#570).
    // Never parse rank or infer a downloadable/installed update.
    private static bool IsInfNameChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-';

    private static bool IsInfForbiddenBoundary(char c) =>
        IsInfNameChar(c) || c is '\\' or '/' or '+';

    private static bool HasVisibleInfFileName(string text)
    {
        // Each possible token is scanned at most 255 characters; tokens
        // cannot restart inside a filename because the preceding character
        // is not a valid boundary. Input is also capped at 48 KiB.
        for (var start = 0; start < text.Length; start++)
        {
            if (!char.IsAsciiLetterOrDigit(text[start]) ||
                (start > 0 && IsInfForbiddenBoundary(text[start - 1])))
                continue;

            var lastDot = Math.Min(text.Length - 4, start + 255);
            for (var dot = start + 1; dot <= lastDot; dot++)
            {
                if (!IsInfNameChar(text[dot]))
                    break;

                if (text[dot] != '.' ||
                    text[dot + 1] is not ('i' or 'I') ||
                    text[dot + 2] is not ('n' or 'N') ||
                    text[dot + 3] is not ('f' or 'F'))
                    continue;

                var after = dot + 4;
                if (after == text.Length ||
                    !IsInfForbiddenBoundary(text[after]))
                    return true;
            }
        }
        return false;
    }

    public enum State
    {
        Available,
        NotVerified
    }

    public sealed record Report(State Status, string DisplayText)
    {
        public bool IsInstallable => false;
        public bool IsNewDriverUpdate => false;
        public bool HasVerifiedExternalOemRanking => false;
    }

    // Never truncate or display a native PnPUtil line containing private
    // text. Detect on ORIGINAL output with invisible Unicode REMOVED:
    // replacing U+200B with a space before inspection could make
    // "PRI<U+200B>VATE_" look harmless while preserving the secret.
    // A public INF/rank line on its own remains visible for diagnostics.
    private static readonly string[] PrivateLineMarkers =
    [
        "PRIVATE_", "SECRET_", "ORDER_", "SERIAL_", "CUSTOMER_",
        "VEN_", "VID_", "DEV_", "PID_", "SUBSYS_", "REV_",
        "FUNC_", "MI_", "INSTANCEID", "SERIALNUMBER"
    ];

    private static bool IsUnsafeUnicode(char ch) =>
        char.IsSurrogate(ch) || ch == '\uFFFD' ||
        char.GetUnicodeCategory(ch) is
            System.Globalization.UnicodeCategory.Format or
            System.Globalization.UnicodeCategory.LineSeparator or
            System.Globalization.UnicodeCategory.ParagraphSeparator;

    private static bool HasSensitiveTextMarker(string text) =>
        text.Contains('@') ||
        text.Contains("/Users/", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("/home/", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("/mnt/", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith("~/", StringComparison.Ordinal) ||
        PrivateLineMarkers.Any(marker =>
            text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string RedactSensitiveLines(string printable, string original)
    {
        var lines = printable.Split('\n');
        var rawLines = original.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            // Split boundaries are unchanged: we only replace unsafe code
            // points with spaces, never create/drop a newline.
            var inspection = new string(rawLines[index]
                .Where(ch => !IsUnsafeUnicode(ch)).ToArray());
            if (ExtraPnpIdentity.IsMatch(inspection))
                lines[index] = "[riga con ID dispositivo oscurata]";
            else if (WebAddress.IsMatch(inspection))
                lines[index] = "[indirizzo oscurato]";
            else if (LocalPath.IsMatch(inspection))
                lines[index] = "[percorso locale oscurato]";
            else if (HasSensitiveTextMarker(inspection))
                lines[index] = "[riga con dati riservati oscurata]";
        }
        return string.Join("\n", lines);
    }

    // A Device Instance ID has enumerator\device-id\instance-id form.
    // Hardware ID alone (two parts), a path, URL or a switch-looking
    // arbitrary string is not a device instance selection. The same
    // guard is used by native SetupAPI and the PnPUtil read-only query.
    // Do not log the rejected value; it may contain a device serial.
    private static bool LooksLikeWindowsInstanceId(string id)
    {
        if (id != id.Trim() ||
            id.Any(ch => char.IsControl(ch) || IsUnsafeUnicode(ch) ||
                (char.IsWhiteSpace(ch) && ch != ' ') ||
                ch is '/' or ':' or '"' or '\''))
            return false;

        // Do not normalize/collapse separators: empty components could
        // disguise an UNC/device path or a truncated Windows ID.
        var parts = id.Split('\\');
        if (parts.Length < 3 || parts[0].Length is < 2 or > 32 ||
            !parts[0].All(ch => char.IsAsciiLetterOrDigit(ch) ||
                ch is '_' or '-'))
            return false;

        return parts.Skip(1).All(part =>
            !string.IsNullOrWhiteSpace(part) &&
            part == part.Trim() && part is not "." and not ".." &&
            !part.StartsWith('-'));
    }

    public static string[] CommandArguments(DriverInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var id = device.DeviceId;
        if (string.IsNullOrWhiteSpace(id) ||
            id.Length > MaxDeviceIdCharacters ||
            !LooksLikeWindowsInstanceId(id))
            throw new InvalidDataException("ID del dispositivo non utilizzabile per PnPUtil.");

        // Explicit fixed read-only allowlist: never /add-driver, /install,
        // /delete-driver, /scan-devices, /restart-device or any shell.
        return ["/enum-devices", "/instanceid", id, "/drivers"];
    }

    public static Report FromCommandResult(
        int exitCode, string? output, DriverInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return new Report(State.NotVerified,
                "Windows non ha restituito i driver per questo dispositivo. " +
                "La verifica non è conclusiva: non significa che manchino driver o aggiornamenti.");

        if (output.Length > MaxOutputCharacters)
            throw new InvalidDataException("Output di PnPUtil oltre il limite.");

        var scrubbed = new StringBuilder(output.Length);
        foreach (var c in output)
        {
            if (c is '\n' or '\r' or '\t')
                scrubbed.Append(c);
            else if (char.IsControl(c) || IsUnsafeUnicode(c))
                scrubbed.Append(' ');
            else
                scrubbed.Append(c);
        }

        // Unknown identifiers, URLs and paths may have private text
        // on BOTH sides of their token. Apply whole-line redaction first,
        // before known-ID substitutions. Never echo native details.
        var sanitized = RedactSensitiveLines(scrubbed.ToString(), output);
        var secrets = new[] { device.DeviceId }
            .Concat(device.HardwareIds)
            .Concat(device.CompatibleIds)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length > 5)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Length);
        foreach (var secret in secrets)
            sanitized = sanitized.Replace(secret!, "[ID dispositivo oscurato]",
                StringComparison.OrdinalIgnoreCase);

        // Headings such as "Matching Drivers:" and a rank number with
        // no remaining driver filename are not verifiable Driver Store
        // evidence. A read-only INF basename survives localization
        // without needing to parse any rank (or guess its meaning).
        // Identity/path/URL-bearing lines have already been removed.
        if (!HasVisibleInfFileName(sanitized))
            return new Report(State.NotVerified,
                "PnPUtil non ha restituito alcun nome INF driver verificabile " +
                "dopo la protezione della privacy. La verifica non è conclusiva: " +
                "non significa che manchino driver o aggiornamenti.");

        return new Report(State.Available,
            "Dati locali prodotti da Windows PnPUtil. La lingua e le etichette " +
            "possono variare; OmegaDrive NON interpreta o ricalcola il ranking. " +
            "Sono pacchetti già presenti in Windows, NON nuovi aggiornamenti online. " +
            "I pacchetti OEM esterni non ancora nel Driver Store non sono inclusi." +
            Environment.NewLine + Environment.NewLine + sanitized.Trim());
    }
}
