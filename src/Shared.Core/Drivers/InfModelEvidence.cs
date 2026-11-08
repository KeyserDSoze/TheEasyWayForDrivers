using System.Text.RegularExpressions;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Inspects model-section IDs in a locally selected INF. Never executes,
/// loads or installs an INF; matching alone does not establish OS, signature
/// or architecture applicability.
/// </summary>
public static partial class InfModelEvidence
{
    public sealed record DeclaredId(
        string Section, string HardwareId, bool IsPrimaryHardwareId = true);

    public const int MaximumPreviewIds = 500;
    public const int MaximumTargetMatches = 4096;
    public const int MaximumTargetDevices = 2048;

    // The general inventory remains bounded to 500 preview entries. Never
    // use this preview to establish ABSENCE of a specific device: large OEM
    // display/network INF files can list its ID after the 500th entry.
    public static IReadOnlyList<DeclaredId> ReadDeclaredIds(string? content) =>
        ReadCore(content, null, MaximumPreviewIds);

    /// <summary>
    /// Read the WHOLE bounded INF to find exactly the IDs already observed
    /// on this Windows PC; only matching IDs are materialized. This lets a
    /// large OEM Models section contain thousands of unrelated IDs without
    /// suppressing the one belonging to the user's device.
    /// </summary>
    public static IReadOnlyList<DeclaredId> ReadMatchingIds(
        string? content, IReadOnlyList<DriverInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (devices.Count > MaximumTargetDevices)
            throw new InvalidDataException("Troppi dispositivi per un confronto INF mirato.");

        var targetIds = devices
            .SelectMany(device => device.HardwareIds.Concat(device.CompatibleIds))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(id => id.Length <= 160)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ReadCore(content, targetIds, MaximumTargetMatches);
    }

    private static IReadOnlyList<DeclaredId> ReadCore(
        string? content, HashSet<string>? targetIds, int limit)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        if (content.Length > OemZipInfEvidence.MaxInfBytes)
            throw new InvalidDataException("Il file INF supera il limite di analisi (4 MiB).");

        if (targetIds is { Count: 0 })
            return [];

        string section = "";
        var declaredSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<(string Section, string Value)>();
        foreach (var line in InfLogicalLines.Read(content))
        {
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
                // Microsoft INF requires each Models section to be EXPLICITLY
                // named in Manufacturer (including its TargetOSVersion
                // decoration). A shared prefix alone does not authorize
                // a different architecture's section.
                var names = right.Split(',').Select(x => x.Trim().Trim('"'))
                    .ToArray();
                if (names.Length == 0 || names[0].Length is < 1 or > 80 ||
                    !names[0].All(IsSectionChar))
                    continue;

                if (names.Length == 1)
                    declaredSections.Add(names[0]);
                else
                {
                    foreach (var decoration in names.Skip(1))
                    {
                        if (decoration.Length is > 0 and <= 80 &&
                            decoration.All(IsSectionChar))
                            declaredSections.Add(names[0] + "." + decoration);
                    }
                }
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
            if (!declaredSections.Contains(modelSection))
                continue;

            var fields = value.Split(',');
            if (fields.Length < 2 || fields[0].Trim().Length == 0)
                continue;

            for (var index = 1; index < fields.Length; index++)
            {
                var id = fields[index].Trim().Trim('"');
                // Windows INF Models: the first ID is the model's hw-id;
                // subsequent IDs in this SAME entry are compatible aliases.
                var isPrimaryHardwareId = index == 1;
                if (id.Length is < 5 or > 160 || !PnPId().IsMatch(id) ||
                    (targetIds is not null && !targetIds.Contains(id)))
                    continue;

                if (!seen.Add(modelSection + "\0" + id + "\0" +
                        (isPrimaryHardwareId ? "H" : "C")))
                    continue;

                if (found.Count >= limit)
                {
                    if (targetIds is null)
                        return found;
                    // Never claim absence after silently truncating matches.
                    throw new InvalidDataException(
                        "L'INF contiene troppe corrispondenze mirate per una verifica attendibile.");
                }

                found.Add(new DeclaredId(
                    modelSection, id.ToUpperInvariant(), isPrimaryHardwareId));
            }
        }

        return found;
    }

    public static int MatchStrength(DriverInfo device, IEnumerable<DeclaredId> declared)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(declared);
        var matches = declared.ToArray();
        // Both sides must describe a PRIMARY hardware ID before "exact"
        // is justified. A compatible-id listed after the first INF model ID
        // must remain weaker even if Windows reports it in HardwareIds.
        if (matches.Any(entry => entry.IsPrimaryHardwareId &&
                device.HardwareIds.Any(id => string.Equals(
                    id?.Trim(), entry.HardwareId,
                    StringComparison.OrdinalIgnoreCase))))
            return 2;

        if (matches.Any(entry => device.HardwareIds.Concat(device.CompatibleIds)
                .Any(id => string.Equals(id?.Trim(), entry.HardwareId,
                    StringComparison.OrdinalIgnoreCase))))
            return 1;

        return 0;
    }

    private static bool IsSectionChar(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '-' or '.';

    // Microsoft INF Models accepts enumerator-specific and *PNP IDs. Keep
    // an explicit bus allowlist for conservative read-only detection. We do
    // not interpret these values as file paths or install directives.
    // HID, Bluetooth, audio, storage and monitors were previously omitted.
    // Printer INF protocols defined by Microsoft are also bus-qualified:
    // USBPRINT, LPTENUM, DOT4PRT, BTHPRINT, WSDPRINT and IEEE-1394.
    // Keep a single separator and ASCII token: never accept instance paths.
    [GeneratedRegex(
        @"^(?:(?:PCI|USB|USBPRINT|LPTENUM|DOT4PRT|BTHPRINT|WSDPRINT|1394|ACPI|HID|HDAUDIO|INTELAUDIO|SOUNDWIRE|SNDW|ACP|BTHENUM|BTHLEDEVICE|BTH|USBSTOR|SCSI|STORAGE|SD|DISPLAY|MONITOR|ROOT|SWD|SWC|VMBUS|SERENUM|I2C|PCMCIA|ISAPNP|UEFI|IDE|MEDIA|UMB)\\[A-Z0-9_&{}+#-]{2,150}|\*(?:PNP|ACPI)[A-Z0-9_&-]{2,120})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PnPId();
}
