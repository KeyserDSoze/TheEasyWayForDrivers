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
    private static readonly Regex ExtraPnpIdentity = new(
        @"(?<![A-Za-z0-9_])[A-Za-z][A-Za-z0-9_]{1,31}\\[A-Za-z0-9_*&%{}#+.\-]{2,}(?:\\[A-Za-z0-9_*&%{}#+.\-]+)*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));

    // A path can contain a username with spaces. Redact the remainder
    // of its line instead of trying to guess the end of a localized path.
    private static readonly Regex LocalPath = new(
        @"(?<![A-Za-z0-9])(?:[A-Za-z]:\\|\\\\)[^\r\n]+",
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

    // Replace the whole diagnostic line, not just the PnP token. A
    // Windows instance suffix may contain spaces, commas, Unicode or
    // metadata which no safe regex can delimit from the rest of a line.
    // Independent rank/INF/version lines remain visible and unparsed.
    private static string RedactPnpIdentityLines(string text)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (ExtraPnpIdentity.IsMatch(lines[index]))
                lines[index] = "[riga con ID dispositivo oscurata]";
        }
        return string.Join("\n", lines);
    }

    public static string[] CommandArguments(DriverInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var id = device.DeviceId;
        if (string.IsNullOrWhiteSpace(id) ||
            id.Length > MaxDeviceIdCharacters ||
            id.Any(char.IsControl) || id.Contains('"') || id.Contains('\''))
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

        // A partial regex replacement is insufficient for unlisted
        // PnP instance identifiers: their serial portion can contain
        // spaces, and surrounding text on the same line can be private.
        // Redact entire lines FIRST, before exact known-ID substitutions,
        // so no residual fragment of a known or unknown instance leaks.
        var sanitized = RedactPnpIdentityLines(scrubbed.ToString());
        var secrets = new[] { device.DeviceId }
            .Concat(device.HardwareIds)
            .Concat(device.CompatibleIds)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length > 5)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Length);
        foreach (var secret in secrets)
            sanitized = sanitized.Replace(secret!, "[ID dispositivo oscurato]",
                StringComparison.OrdinalIgnoreCase);

        // Even a successful read-only PnPUtil query may contain additional
        // PnP IDs and local paths. Keep rank/INF text uninterpreted, but
        // don't let unlisted identifiers leak in this diagnostic panel.
        sanitized = LocalPath.Replace(sanitized, "[percorso locale oscurato]");
        sanitized = WebAddress.Replace(sanitized, "[indirizzo oscurato]");

        if (string.IsNullOrWhiteSpace(sanitized))
            return new Report(State.NotVerified,
                "La risposta di Windows è vuota dopo la protezione degli identificativi.");

        return new Report(State.Available,
            "Dati locali prodotti da Windows PnPUtil. La lingua e le etichette " +
            "possono variare; OmegaDrive NON interpreta o ricalcola il ranking. " +
            "Sono pacchetti già presenti in Windows, NON nuovi aggiornamenti online. " +
            "I pacchetti OEM esterni non ancora nel Driver Store non sono inclusi." +
            Environment.NewLine + Environment.NewLine + sanitized.Trim());
    }
}
