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

    // Do not preserve any portion of a line containing a PnP ID,
    // local path or URL. Windows output may include private labels
    // before these values, as well as after them. Independent INF,
    // version and rank lines remain readable without interpretation.
    private static string RedactSensitiveLines(string text)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (ExtraPnpIdentity.IsMatch(lines[index]))
                lines[index] = "[riga con ID dispositivo oscurata]";
            else if (WebAddress.IsMatch(lines[index]))
                lines[index] = "[indirizzo oscurato]";
            else if (LocalPath.IsMatch(lines[index]))
                lines[index] = "[percorso locale oscurato]";
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
            id.Any(ch => char.IsControl(ch) || char.IsSurrogate(ch) ||
                ch is '/' or ':' or '"' or '\'' or '\u202A' or '\u202B' or
                      '\u202C' or '\u202D' or '\u202E' or
                      '\u2066' or '\u2067' or '\u2068' or '\u2069'))
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
            else if (char.IsControl(c) || c is '\u202A' or '\u202B' or
                         '\u202C' or '\u202D' or '\u202E' or
                         '\u2066' or '\u2067' or '\u2068' or '\u2069')
                scrubbed.Append(' ');
            else
                scrubbed.Append(c);
        }

        // Unknown identifiers, URLs and paths may have private text
        // on BOTH sides of their token. Apply whole-line redaction first,
        // before known-ID substitutions. Never echo native details.
        var sanitized = RedactSensitiveLines(scrubbed.ToString());
        var secrets = new[] { device.DeviceId }
            .Concat(device.HardwareIds)
            .Concat(device.CompatibleIds)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length > 5)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Length);
        foreach (var secret in secrets)
            sanitized = sanitized.Replace(secret!, "[ID dispositivo oscurato]",
                StringComparison.OrdinalIgnoreCase);

        // A successful native exit code is not by itself evidence that
        // any driver/rank information survived privacy filtering. If ALL
        // lines were device IDs, URLs or paths, showing "Available" would
        // falsely imply a verified Driver Store result. Leave native rank
        // text uninterpreted: this is only a redaction-only guard.
        var visibleContent = sanitized.Split('\n').Any(line =>
        {
            var value = line.Trim();
            return value.Length > 0 &&
                value is not "[riga con ID dispositivo oscurata]" and
                    not "[indirizzo oscurato]" and
                    not "[percorso locale oscurato]";
        });
        if (!visibleContent)
            return new Report(State.NotVerified,
                "PnPUtil non ha restituito informazioni sui driver utilizzabili " +
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
