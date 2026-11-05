using System.Text;
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

        var sanitized = scrubbed.ToString();
        var secrets = new[] { device.DeviceId }
            .Concat(device.HardwareIds)
            .Concat(device.CompatibleIds)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length > 5)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Length);
        foreach (var secret in secrets)
            sanitized = sanitized.Replace(secret!, "[ID dispositivo oscurato]",
                StringComparison.OrdinalIgnoreCase);

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
