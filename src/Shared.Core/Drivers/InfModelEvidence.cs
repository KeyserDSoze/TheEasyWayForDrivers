using System.Text.RegularExpressions;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Inspects model-section IDs in a locally selected INF. Never executes,
/// loads or installs an INF; matching alone does not establish OS, signature
/// or architecture applicability.
/// </summary>
public static partial class InfModelEvidence
{
    public sealed record DeclaredId(string Section, string HardwareId);

    public static IReadOnlyList<DeclaredId> ReadDeclaredIds(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        if (content.Length > 1024 * 1024)
            throw new InvalidDataException("Il file INF supera il limite di analisi (1 MiB).");

        string section = "";
        var declaredSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<(string Section, string Value)>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Split(';', 2)[0].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            var right = line[(eq + 1)..].Trim();

            if (section.Equals("Manufacturer", StringComparison.OrdinalIgnoreCase))
            {
                var root = right.Split(',', 2)[0].Trim();
                // Manufacturer may declare Models, NTamd64, NTarm64 etc.
                if (root.Length is > 0 and <= 80 && root.All(IsSectionChar))
                    declaredSections.Add(root);
            }
            else if (!string.IsNullOrWhiteSpace(section))
                lines.Add((section, right));
        }

        if (declaredSections.Count == 0)
            return [];

        var found = new List<DeclaredId>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (modelSection, value) in lines)
        {
            if (!declaredSections.Any(root =>
                    modelSection.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                    modelSection.StartsWith(root + ".", StringComparison.OrdinalIgnoreCase)))
                continue;

            // A model assignment has a DDInstall name before the comma and
            // one or more PnP identifiers after it, not a device title alone.
            var fields = value.Split(',');
            if (fields.Length < 2 || fields[0].Trim().Length == 0)
                continue;

            foreach (var field in fields.Skip(1))
            {
                var id = field.Trim().Trim('"');
                if (id.Length is < 5 or > 160 || !PnPId().IsMatch(id))
                    continue;
                if (seen.Add(modelSection + "\0" + id))
                    found.Add(new DeclaredId(modelSection, id.ToUpperInvariant()));
                if (found.Count >= 500)
                    return found;
            }
        }
        return found;
    }

    public static int MatchStrength(DriverInfo device, IEnumerable<DeclaredId> declared)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(declared);
        var ids = declared.Select(x => x.HardwareId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (device.HardwareIds.Any(id => ids.Contains(id.Trim()))) return 2;
        if (device.CompatibleIds.Any(id => ids.Contains(id.Trim()))) return 1;
        return 0;
    }

    private static bool IsSectionChar(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '-' or '.';

    // Microsoft INF Models accepts enumerator-specific and *PNP IDs. Keep
    // an explicit bus allowlist for conservative read-only detection. We do
    // not interpret these values as file paths or install directives.
    // HID, Bluetooth, audio, storage and monitors were previously omitted.
    [GeneratedRegex(
        @"^(?:(?:PCI|USB|ACPI|HID|HDAUDIO|BTHENUM|BTHLEDEVICE|BTH|USBSTOR|SCSI|STORAGE|SD|DISPLAY|MONITOR|ROOT|SWD|VMBUS|SERENUM|I2C|PCMCIA|ISAPNP|UEFI|IDE|MEDIA|UMB)\\[A-Z0-9_&{}+#-]{2,150}|\*(?:PNP|ACPI)[A-Z0-9_&-]{2,120})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PnPId();
}
