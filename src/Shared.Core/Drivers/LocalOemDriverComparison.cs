using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Read-only comparison of local OEM ZIP INF declarations with installed PnP
/// hardware and Windows architecture/build. It never extracts, executes,
/// downloads, imports or installs drivers. The original package's authenticity
/// is NOT established by finding a CAT filename in its ZIP directory.
/// </summary>
public static class LocalOemDriverComparison
{
    public enum VersionRelation { NewerHint, Equal, OlderHint, Unavailable }

    public sealed record Finding(
        string DeviceName,
        string InfName,
        string InstalledVersion,
        string PackageVersion,
        int HardwareMatchStrength,
        InfPlatformPreflight.Verdict PlatformVerdict,
        VersionRelation VersionComparison)
    {
        public bool HasExactHardwareId => HardwareMatchStrength == 2;
        public bool IsPotentialNewer => HasExactHardwareId &&
            PlatformVerdict == InfPlatformPreflight.Verdict.ModelSectionPotentialMatch &&
            VersionComparison == VersionRelation.NewerHint;

        public string Summary
        {
            get
            {
                var identity = HasExactHardwareId ? "Hardware ID esatto" : "Compatible ID";
                var os = PlatformVerdict switch
                {
                    InfPlatformPreflight.Verdict.ModelSectionPotentialMatch =>
                        "architettura/Windows dichiarati coerenti",
                    InfPlatformPreflight.Verdict.ArchitectureMismatch =>
                        "ARCHITETTURA DIVERSA",
                    InfPlatformPreflight.Verdict.MinimumWindowsNotMet =>
                        "Windows troppo vecchio per la sezione INF",
                    _ => "applicabilità Windows non determinata"
                };
                var version = VersionComparison switch
                {
                    VersionRelation.NewerHint => "numero versione superiore (DA VERIFICARE)",
                    VersionRelation.Equal => "stesso numero versione",
                    VersionRelation.OlderHint => "numero versione inferiore",
                    _ => "versioni non confrontabili"
                };
                return $"{DeviceName} · {identity} · {os} · " +
                    $"installato {InstalledVersion} / INF {PackageVersion} · {version}";
            }
        }
    }

    public sealed record Report(
        int InfExamined,
        int DevicesExamined,
        IReadOnlyList<Finding> Findings,
        int AdditionalMatchesOmitted,
        IReadOnlyList<string> Notes,
        int RejectedInfCount = 0,
        int InfWithDeclaredIds = 0,
        int LegacyAnsiCount = 0,
        int MatchingDevicesOmitted = 0)
    {
        // DevicesWithEvidence is the number of VISIBLE results, not the
        // complete OEM match count when the 80-row display budget applies.
        public int DevicesWithEvidence => Findings.Count;
        public int TotalMatchedDevices => Findings.Count + MatchingDevicesOmitted;
        public int PotentialNewerDevices => Findings.Count(f => f.IsPotentialNewer);
        public bool IsInstallable => false;
        public bool IsVerifiedCompatible => false;
        public string Summary =>
            $"{InfExamined} INF presenti ({InfExamined - RejectedInfCount} leggibili, " +
            $"{RejectedInfCount} scartati, {InfWithDeclaredIds} con ID PnP, " +
            $"{LegacyAnsiCount} con sintassi ANSI recuperata); " +
            $"{DevicesExamined} dispositivi Windows; " +
            $"{TotalMatchedDevices} dispositivi con corrispondenze PnP " +
            $"({DevicesWithEvidence} mostrati, {MatchingDevicesOmitted} oltre il limite); " +
            $"{PotentialNewerDevices} versioni numericamente superiori da verificare " +
            "nelle righe mostrate. " +
            "Il risultato NON certifica firma, compatibilità, necessità o installabilità.";
    }

    /// <summary>
    /// Each input is an archive already obtained independently from a trusted
    /// manufacturer. The caller chooses the file on the PC (no remote fetch).
    /// Unsupported archive formats are rejected rather than called empty.
    /// </summary>
    public static Report Compare(
        Stream archive,
        IReadOnlyList<DriverInfo> installed,
        string osArchitecture,
        int osMajor,
        int osMinor,
        int osBuild)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(installed);
        if (installed.Count > 2048)
            throw new InvalidDataException("Troppi dispositivi per il confronto OEM.");

        var evidence = OemZipInfEvidence.Inspect(archive);
        if (!archive.CanSeek || !archive.CanRead)
            throw new InvalidDataException("Archivio OEM non leggibile.");

        // OemZipInfEvidence does not inspect INF inside CAB/MSI/EXE. Never
        // mistake an unsupported format for '0 matching drivers'.
        archive.Position = 0;
        var kind = OemArchiveInspector.Inspect(archive);
        if (kind.ArchiveFormat != OemArchiveInspector.Format.Zip)
            throw new NotSupportedException(
                "Solo ZIP con file INF leggibili in sola lettura. " +
                "CAB, EXE e MSI richiedono un flusso di verifica separato.");

        // PnP inventory is bounded to 2048 entries. Collect the best
        // evidence for EVERY distinct device before the 80-row display cap:
        // the original first-80 logic silently hid NVIDIA/Killer when an
        // Intel-dominated INF/inventory happened to be enumerated first.
        var candidates = new Dictionary<string, (DriverInfo Device, Finding Finding)>(
            StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        int extra = 0;
        int lateRejected = 0;
        int lateRejectedWithIds = 0;
        int processedBytes = 0;
        archive.Position = 0;
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        var catalogPaths = kind.Entries
            .Where(entry => entry.Kind == OemArchiveInspector.FileKind.Cat)
            .Select(entry => entry.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var inf in evidence.InfFiles)
        {
            if (!inf.IsReadable)
                continue;

            // The 500-ID general preview may not contain the target device
            // even when a later INF Models line does. Inspect every readable
            // INF, then only retain exact IDs from this PC's PnP inventory.
            var entry = zip.GetEntry(inf.Path) ??
                throw new InvalidDataException("INF indicato nell'indice ZIP non disponibile.");
            if (entry.Length > OemZipInfEvidence.MaxInfBytes)
                throw new InvalidDataException("INF troppo grande per la comparazione.");

            using var input = entry.Open();
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int size;
            while ((size = input.Read(chunk, 0, chunk.Length)) > 0)
            {
                processedBytes = checked(processedBytes + size);
                if (buffer.Length + size > OemZipInfEvidence.MaxInfBytes ||
                    processedBytes > OemZipInfEvidence.MaxTotalInfBytes)
                    throw new InvalidDataException("Limite di decompressione INF raggiunto.");
                buffer.Write(chunk, 0, size);
            }

            // ZIP, CAB (converted to bounded in-memory ZIP) and extracted
            // OEM folders now use the SAME conservative decoder as the
            // inventory pass; no extra silent UTF-8-only filter.
            var text = OemInfTextDecoder.Decode(buffer.ToArray()).Text;

            IReadOnlyList<InfModelEvidence.DeclaredId> matchingIds;
            try
            {
                // The general 500-ID preview can succeed while a targeted
                // scan exceeds the separate 4096-match safety budget.
                // Discard all matching evidence from THAT INF, never silently
                // treat a truncated subset as verified device coverage.
                matchingIds = InfModelEvidence.ReadMatchingIds(text, installed);
            }
            catch (InvalidDataException)
            {
                lateRejected++;
                if (inf.DeclaredIds.Count > 0)
                    lateRejectedWithIds++;
                continue;
            }

            if (matchingIds.Count == 0)
                continue;
            var interested = installed.Where(device =>
                InfModelEvidence.MatchStrength(device, matchingIds) > 0).ToArray();
            if (interested.Length == 0)
                continue;

            var cat = InfPlatformPreflight.GetCatalogFileName(text, osArchitecture);
            var prefix = inf.Path.Contains('/') ?
                inf.Path[..(inf.Path.LastIndexOf('/') + 1)] : "";
            var hasCatalog = cat is not null && catalogPaths.Contains(prefix + cat);

            foreach (var device in interested)
            {
                var report = InfPlatformPreflight.Assess(
                    text, device, osArchitecture, osMajor, osMinor, osBuild,
                    hasCatalog);
                if (report.Result == InfPlatformPreflight.Verdict.NoHardwareMatch)
                    continue;

                var package = ReadPackageVersion(report.DriverVersion);
                var comparison = CompareVersions(device.DriverVersion, package);
                var finding = new Finding(
                    TrimUntrusted(device.Name, 90),
                    TrimUntrusted(inf.Path.Split('/').Last(), 90),
                    TrimUntrusted(device.DriverVersion, 50),
                    package ?? "non dichiarata",
                    report.HardwareMatchStrength,
                    report.Result,
                    comparison);

                var key = device.DeviceId;
                if (candidates.TryGetValue(key, out var previous))
                {
                    if (Priority(finding) > Priority(previous.Finding))
                        candidates[key] = (device, finding);
                    extra++; // another INF for an already known PnP instance
                }
                else
                {
                    candidates.Add(key, (device, finding));
                }
            }
        }

        if (evidence.InfFiles.Any(inf =>
                inf.DeclaredIds.Count >= InfModelEvidence.MaximumPreviewIds))
            notes.Add(
                "Almeno un INF supera o raggiunge i 500 ID dell'anteprima: " +
                "il confronto mirato ha controllato l'intero INF per gli ID " +
                "PnP di questo PC, senza usare l'anteprima come prova di assenza.");
        if (evidence.RejectedInfCount + lateRejected != 0)
            notes.Add($"{evidence.RejectedInfCount + lateRejected} INF " +
                "non leggibili/scartati (inclusi errori nel confronto mirato); " +
                "il report è parziale, non una prova di assenza driver.");
        if (evidence.LegacyAnsiCount != 0)
            notes.Add($"{evidence.LegacyAnsiCount} INF con testo locale ANSI: " +
                "recuperata solo la sintassi ASCII, senza determinare la codepage.");
        if (extra > 0)
            notes.Add("Più INF possono riferirsi allo stesso dispositivo; una sola riga per dispositivo.");

        var omitted = Math.Max(0, candidates.Count - MaximumDisplayedFindings);
        if (omitted > 0)
            notes.Add($"{omitted} altri dispositivi con corrispondenza PnP " +
                "non mostrati per il limite di 80 righe. Il riepilogo " +
                "non è esaustivo: nessuna assenza driver è dimostrata.");
        notes.Add("La presenza di CAT nello ZIP non prova che il file sia firmato o che il pacchetto sia autentico.");

        return new Report(
            evidence.InfFiles.Count,
            installed.Count,
            SelectVisibleFindings(candidates.Values),
            extra + omitted,
            notes.Distinct().ToArray(),
            evidence.RejectedInfCount + lateRejected,
            evidence.InfWithDeclaredIds - lateRejectedWithIds,
            evidence.LegacyAnsiCount,
            omitted);
    }

    public const int MaximumDisplayedFindings = 80;

    /// <summary>
    /// Choose the 80 visible OEM diagnostic rows only AFTER matching all
    /// bounded Windows devices. Prioritize potentially newer exact matches
    /// within each vendor, but allocate rows round-robin across vendors so
    /// a large Intel INF cannot hide the one NVIDIA/Realtek/Killer device.
    /// This is presentation fairness, NOT Windows SetupAPI driver ranking.
    /// </summary>
    private static IReadOnlyList<Finding> SelectVisibleFindings(
        IEnumerable<(DriverInfo Device, Finding Finding)> candidates)
    {
        var groups = candidates
            .GroupBy(candidate =>
                VendorSourceDiscovery.GetRecognizedSupportVendor(candidate.Device)
                    ?? "Altro · " + (candidate.Device.DeviceClass ?? "N/D"),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Vendor = group.Key,
                Rows = group
                    .OrderByDescending(item => item.Finding.IsPotentialNewer)
                    .ThenByDescending(item => Priority(item.Finding))
                    .ThenByDescending(item => item.Device.NeedsAttention)
                    .ThenBy(item => item.Finding.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Finding.InfName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Device.DeviceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .OrderByDescending(group => group.Rows.Any(row => row.Finding.IsPotentialNewer))
            .ThenByDescending(group => group.Rows.Max(row => Priority(row.Finding)))
            .ThenBy(group => group.Vendor, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selected = new List<Finding>(MaximumDisplayedFindings);
        for (var depth = 0; selected.Count < MaximumDisplayedFindings; depth++)
        {
            var any = false;
            foreach (var group in groups)
            {
                if (depth >= group.Rows.Length)
                    continue;
                any = true;
                selected.Add(group.Rows[depth].Finding);
                if (selected.Count == MaximumDisplayedFindings)
                    break;
            }
            if (!any)
                break;
        }

        return selected;
    }

    private static int Priority(Finding finding) =>
        (finding.PlatformVerdict == InfPlatformPreflight.Verdict.ModelSectionPotentialMatch ? 10 : 0) +
        (finding.HasExactHardwareId ? 5 : 0) +
        (finding.VersionComparison == VersionRelation.NewerHint ? 2 : 0);

    private static string TrimUntrusted(string? text, int maximum)
    {
        var cleaned = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (cleaned.Length == 0)
            return "N/D";
        return cleaned.Length > maximum ? cleaned[..maximum] : cleaned;
    }

    private static readonly Regex DriverVer = new(
        @"\A\s*\d{1,2}/\d{1,2}/\d{4}\s*,\s*(?<v>\d{1,6}(?:\.\d{1,6}){2,4})\s*\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static string? ReadPackageVersion(string? declared)
    {
        if (declared is null || declared.Length > 90)
            return null;
        var found = DriverVer.Match(declared);
        return found.Success ? found.Groups["v"].Value : null;
    }

    private static VersionRelation CompareVersions(string? installed, string? package)
    {
        if (installed is null || package is null)
            return VersionRelation.Unavailable;
        var a = installed.Trim().Split('.');
        var b = package.Split('.');
        // Driver versions must be four numeric components; otherwise
        // Windows SetupAPI's real ranking rules cannot be approximated safely.
        if (a.Length != 4 || b.Length != 4)
            return VersionRelation.Unavailable;
        for (var i = 0; i < 4; i++)
        {
            if (a[i].Length is < 1 or > 6 || b[i].Length is < 1 or > 6 ||
                !int.TryParse(a[i], out var oldPart) ||
                !int.TryParse(b[i], out var newPart))
                return VersionRelation.Unavailable;
            if (newPart > oldPart)
                return VersionRelation.NewerHint;
            if (newPart < oldPart)
                return VersionRelation.OlderHint;
        }
        return VersionRelation.Equal;
    }
}
