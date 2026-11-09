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
            // PCI class-only/vendor-only tokens belong to thousands of
            // unrelated devices. They are useful metadata, but NOT an
            // INF-to-device match, even if WMI lists them as Hardware IDs.
            .Where(DriverUpdateMatcher.IsCorrelatableHardwareId)
            .Select(id => id.Trim())
            .Where(id => id.Length <= 160)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ReadCore(content, targetIds, MaximumTargetMatches);
    }

    /// <summary>
    /// Strictly diagnostic fallback when Windows reports a specific
    /// SUBSYS/REV HardwareID but no generic Compatible ID. Only a primary
    /// INF Models declaration with EXACTLY the public VEN+DEV/model key can
    /// be considered; other OEM SUBSYS values never collapse into a match.
    /// This is NOT compatibility, Windows ranking or a newer-driver claim.
    /// </summary>
    public static IReadOnlyList<DeclaredId> ReadPublicModelReviewIds(
        string? content, IReadOnlyList<DriverInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (devices.Count > MaximumTargetDevices)
            throw new InvalidDataException(
                "Troppi dispositivi per la revisione INF per modello.");

        var publicKeys = devices
            .SelectMany(WuaGenericModelCorrelation.PublicReviewModelKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (publicKeys.Count == 0)
            return [];

        return ReadCore(content, publicKeys, MaximumTargetMatches)
            .Where(declared => declared.IsPrimaryHardwareId &&
                DriverUpdateMatcher.IsCorrelatableHardwareId(declared.HardwareId) &&
                string.Equals(
                    MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(
                        declared.HardwareId),
                    declared.HardwareId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static bool IsPublicModelOnlyReviewMatch(
        DriverInfo device, IReadOnlyList<DeclaredId> publicModelIds)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(publicModelIds);
        if (publicModelIds.Count == 0)
            return false;

        var models = WuaGenericModelCorrelation.PublicReviewModelKeys(device);
        return models.Count > 0 && publicModelIds.Any(declared =>
            declared.IsPrimaryHardwareId &&
            models.Contains(declared.HardwareId) &&
            string.Equals(
                MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(
                    declared.HardwareId),
                declared.HardwareId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A specific PCI/HDAUDIO/INTELAUDIO/SWC PnP ID may contain an OEM
    /// SUBSYS plus a terminal two-hex REV, while the INF's first model
    /// declaration omits ONLY the revision. Never strip the subsystem:
    /// this produces a review hint, NOT a Windows-compatible ID.
    /// </summary>
    public static IReadOnlyList<DeclaredId> ReadRevisionlessSubsysReviewIds(
        string? content, IReadOnlyList<DriverInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (devices.Count > MaximumTargetDevices)
            throw new InvalidDataException(
                "Troppi dispositivi per la revisione INF SUBSYS.");

        var keys = devices.SelectMany(RevisionlessSubsysReviewKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.Count == 0)
            return [];

        return ReadCore(content, keys, MaximumTargetMatches)
            .Where(declared => declared.IsPrimaryHardwareId)
            .ToArray();
    }

    public static bool IsRevisionlessSubsysReviewMatch(
        DriverInfo device, IReadOnlyList<DeclaredId> reviewIds)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(reviewIds);
        if (reviewIds.Count == 0)
            return false;

        var keys = RevisionlessSubsysReviewKeys(device);
        return keys.Count > 0 && reviewIds.Any(entry =>
            entry.IsPrimaryHardwareId && keys.Contains(entry.HardwareId));
    }

    private static IReadOnlySet<string> RevisionlessSubsysReviewKeys(
        DriverInfo device)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in device.HardwareIds)
        {
            var key = TryGetRevisionlessSubsysKey(id);
            if (key is not null)
                keys.Add(key);
        }
        return keys;
    }

    private static string? TryGetRevisionlessSubsysKey(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 160)
            return null;

        // The shared strict model parser validates the entire ID and
        // rejects private instance paths, fake attributes and malformed
        // VEN/DEV, SUBSYS or REV fields before dropping even one token.
        var model = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(value);
        if (model is null ||
            !(model.StartsWith(@"PCI\", StringComparison.Ordinal) ||
              model.StartsWith(@"HDAUDIO\", StringComparison.Ordinal) ||
              model.StartsWith(@"INTELAUDIO\", StringComparison.Ordinal) ||
              model.StartsWith(@"SWC\", StringComparison.Ordinal)))
            return null;

        var prefix = model + "&SUBSYS_";
        const string revision = "&REV_";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            value.Length != prefix.Length + 8 + revision.Length + 2 ||
            !value.AsSpan(prefix.Length, 8).ToString().All(Uri.IsHexDigit) ||
            !value.AsSpan(prefix.Length + 8, revision.Length)
                .Equals(revision.AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            !value.AsSpan(value.Length - 2).ToString().All(Uri.IsHexDigit))
            return null;

        var key = value[..(prefix.Length + 8)].ToUpperInvariant();
        return DriverUpdateMatcher.IsCorrelatableHardwareId(key) ? key : null;
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
                    (targetIds is not null &&
                        (!DriverUpdateMatcher.IsCorrelatableHardwareId(id) ||
                         !targetIds.Contains(id))))
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
        // Guard also direct callers passing their own DeclaredId preview:
        // PCI class-only tokens must never imply an exact device match.
        var matches = declared.Where(entry =>
            DriverUpdateMatcher.IsCorrelatableHardwareId(entry.HardwareId))
            .ToArray();
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
