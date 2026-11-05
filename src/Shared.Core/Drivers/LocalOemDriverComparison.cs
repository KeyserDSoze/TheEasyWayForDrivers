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
        IReadOnlyList<string> Notes)
    {
        public int DevicesWithEvidence => Findings.Count;
        public int PotentialNewerDevices => Findings.Count(f => f.IsPotentialNewer);
        public bool IsInstallable => false;
        public bool IsVerifiedCompatible => false;
        public string Summary =>
            $"{InfExamined} INF analizzati; {DevicesExamined} dispositivi Windows; " +
            $"{DevicesWithEvidence} dispositivi con corrispondenze PnP; " +
            $"{PotentialNewerDevices} versioni numericamente superiori da verificare. " +
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

        var candidates = new Dictionary<string, Finding>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        int extra = 0;
        int processedBytes = 0;
        archive.Position = 0;
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        var catalogPaths = kind.Entries
            .Where(entry => entry.Kind == OemArchiveInspector.FileKind.Cat)
            .Select(entry => entry.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var inf in evidence.InfFiles)
        {
            if (inf.DeclaredIds.Count == 0)
                continue;

            var interested = installed.Where(device =>
                InfModelEvidence.MatchStrength(device, inf.DeclaredIds) > 0).ToArray();
            if (interested.Length == 0)
                continue;

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

            string text;
            var bytes = buffer.ToArray();
            try
            {
                if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                {
                    text = new UnicodeEncoding(false, true, true)
                        .GetString(bytes, 2, bytes.Length - 2);
                }
                else
                {
                    var skipBom = bytes.Length >= 3 && bytes[0] == 0xEF &&
                        bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                    text = new UTF8Encoding(false, true)
                        .GetString(bytes, skipBom, bytes.Length - skipBom);
                }
            }
            catch (DecoderFallbackException)
            {
                notes.Add("Un file INF usa una codifica non supportata; ignorato.");
                continue;
            }

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
                    if (Priority(finding) > Priority(previous))
                        candidates[key] = finding;
                    extra++;
                }
                else if (candidates.Count < 80)
                {
                    candidates.Add(key, finding);
                }
                else
                {
                    extra++;
                }
            }
        }

        if (evidence.Warnings.Count != 0)
            notes.Add("Alcuni INF non erano leggibili; il report è incompleto.");
        if (extra > 0)
            notes.Add("Più INF possono riferirsi allo stesso dispositivo; una sola riga per dispositivo.");
        notes.Add("La presenza di CAT nello ZIP non prova che il file sia firmato o che il pacchetto sia autentico.");

        return new Report(
            evidence.InfFiles.Count,
            installed.Count,
            candidates.Values.OrderByDescending(Priority)
                .ThenBy(f => f.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            extra,
            notes.Distinct().ToArray());
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
