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
        // Ephemeral index of the in-memory Windows inventory, NEVER the
        // PnP Instance ID itself. Enables truthful cross-archive deduplication.
        public int DeviceOrdinal { get; init; } = -1;
        // True only for a same-SUBSYS INF primary ID with the terminal
        // two-hex PnP revision omitted; never proves Windows compatibility.
        public bool IsSubsysRevisionReview { get; init; }
        public bool HasExactHardwareId => HardwareMatchStrength == 2;
        // Model-only PnP evidence is NOT an INF compatible ID, an applicable
        // Windows driver or a numerically newer offer.
        public bool IsModelOnlyReview => HardwareMatchStrength == 0;
        public bool IsPotentialNewer => HasExactHardwareId &&
            PlatformVerdict == InfPlatformPreflight.Verdict.ModelSectionPotentialMatch &&
            VersionComparison == VersionRelation.NewerHint;

        public string Summary
        {
            get
            {
                var identity = IsSubsysRevisionReview
                    ? "SOLO SUBSYS OEM senza REV (revisione manuale, NON compatibilità)"
                    : IsModelOnlyReview
                        ? "SOLO modello pubblico INF (revisione manuale, NON compatibilità)"
                        : HasExactHardwareId ? "Hardware ID esatto" : "Compatible ID";
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
        // Complete ephemeral device ordinals, including beyond the 80-row
        // display cap; never persist or expose the original DeviceId.
        // Null indicates an older/unverified report that cannot safely
        // supply exact distinct-device counts across different archives.
        public IReadOnlyList<int>? MatchedDeviceOrdinals { get; init; }
        // Uncapped read-only ordinals with actual INF Hardware/Compatible
        // ID evidence. Null in legacy reports: coverage is unverified.
        // Ordinals are in-memory indices, never PnP IDs or serial numbers.
        public IReadOnlyList<int>? PnpMatchedDeviceOrdinals { get; init; }

        // This flag never claims an absent driver from a bounded archive
        // scan that deliberately stops after two nested ZIP/CAB levels.
        public bool HasUninspectedNestedArchives { get; init; }

        // Anonymous, typed coverage gaps. These properties intentionally
        // contain no file paths, PnP IDs, original OEM names or order IDs.
        // Zero is the default for old/direct-INF comparison reports.
        public int UninspectedNestedExecutables { get; init; }
        public int UninspectedDeeperArchives { get; init; }
        public int UninspectedProprietaryPackages { get; init; }

        // DevicesWithEvidence is the number of VISIBLE results, not the
        // complete OEM match count when the 80-row display budget applies.
        public int DevicesWithEvidence => Findings.Count;
        public int TotalMatchedDevices => Findings.Count + MatchingDevicesOmitted;
        public int PotentialNewerDevices => Findings.Count(f => f.IsPotentialNewer);
        public int ModelOnlyReviewDevices => Findings.Count(f => f.IsModelOnlyReview);
        public int SubsysRevisionReviewDevices =>
            Findings.Count(f => f.IsSubsysRevisionReview);
        public bool IsInstallable => false;
        public bool IsVerifiedCompatible => false;
        public string Summary =>
            $"{InfExamined} INF presenti ({InfExamined - RejectedInfCount} leggibili, " +
            $"{RejectedInfCount} scartati, {InfWithDeclaredIds} con ID PnP, " +
            $"{LegacyAnsiCount} con sintassi ANSI recuperata); " +
            $"{DevicesExamined} dispositivi Windows; " +
            $"{TotalMatchedDevices} dispositivi con corrispondenze PnP o " +
            $"SOLO modello pubblico o SUBSYS senza REV da verificare " +
            $"({DevicesWithEvidence} mostrati, " +
            $"{MatchingDevicesOmitted} oltre il limite; " +
            $"{ModelOnlyReviewDevices} indizi manuali modello pubblico/SUBSYS " +
            $"fra i mostrati ({SubsysRevisionReviewDevices} con SUBSYS uguale " +
            "e REV omessa), NON compatibilità confermata); " +
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

        // Filter contradictory same-instance PnP models BEFORE INF
        // matching. Never merge HWIDs from different Windows rows or
        // select an arbitrary NVIDIA/Realtek vendor by enumeration order.
        // Keep original inventory positions for ZIP/CAB batch ordinal
        // consistency: do not re-index the trusted subset.
        var trusted = PnpDistinctTrustedDevices.Create(installed);
        var uniqueInstances = installed
            .Where(device => !string.IsNullOrWhiteSpace(device.DeviceId))
            .Select(device => device.DeviceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var excludedAmbiguous = uniqueInstances - trusted.Count;

        var inventoryOrdinals = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < installed.Count; index++)
        {
            var id = installed[index].DeviceId;
            if (!string.IsNullOrWhiteSpace(id) &&
                !inventoryOrdinals.ContainsKey(id))
                inventoryOrdinals.Add(id, index);
        }

        // PnP inventory is bounded to 2048 entries. Collect the best
        // evidence for EVERY distinct device before the 80-row display cap:
        // the original first-80 logic silently hid NVIDIA/Killer when an
        // Intel-dominated INF/inventory happened to be enumerated first.
        var candidates = new Dictionary<string, (DriverInfo Device, Finding Finding)>(
            StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        if (excludedAmbiguous > 0)
            notes.Add($"{excludedAmbiguous} istanze PnP con vendor " +
                "contraddittori escluse dal confronto INF: copertura " +
                "parziale, nessuna prova di assenza o di driver applicabile.");
        int extra = 0;
        int lateRejected = 0;
        int lateRejectedWithIds = 0;
        int processedBytes = 0;
        archive.Position = 0;
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        // Canonical paths from the validated index also match Windows-style ZIP names.
        var entries = OemArchiveInspector.CreateValidatedZipEntryIndex(zip);
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
            if (!entries.TryGetValue(inf.Path, out var entry))
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
            IReadOnlyList<InfModelEvidence.DeclaredId> modelReviewIds;
            IReadOnlyList<InfModelEvidence.DeclaredId> subsysReviewIds;
            try
            {
                // The general 500-ID preview can succeed while a targeted
                // scan exceeds the separate 4096-match safety budget.
                // Discard all matching evidence from THAT INF, never silently
                // treat a truncated subset as verified device coverage.
                matchingIds = InfModelEvidence.ReadMatchingIds(text, trusted);
                // Specific SUBSYS/REV without Compatible IDs can hide an
                // INF's generic VEN+DEV model. Read this SEPARATELY, as
                // manual review evidence with no install/update authority.
                modelReviewIds = InfModelEvidence.ReadPublicModelReviewIds(
                    text, trusted);
                subsysReviewIds = InfModelEvidence.ReadRevisionlessSubsysReviewIds(
                    text, trusted);
            }
            catch (InvalidDataException)
            {
                lateRejected++;
                if (inf.DeclaredIds.Count > 0)
                    lateRejectedWithIds++;
                continue;
            }

            if (matchingIds.Count == 0 && modelReviewIds.Count == 0 &&
                subsysReviewIds.Count == 0)
                continue;
            var interested = trusted.Where(device =>
                InfModelEvidence.MatchStrength(device, matchingIds) > 0 ||
                InfModelEvidence.IsPublicModelOnlyReviewMatch(
                    device, modelReviewIds) ||
                InfModelEvidence.IsRevisionlessSubsysReviewMatch(
                    device, subsysReviewIds)).ToArray();
            if (interested.Length == 0)
                continue;

            var cat = InfPlatformPreflight.GetCatalogFileName(text, osArchitecture);
            var prefix = inf.Path.Contains('/') ?
                inf.Path[..(inf.Path.LastIndexOf('/') + 1)] : "";
            var hasCatalog = cat is not null && catalogPaths.Contains(prefix + cat);

            foreach (var device in interested)
            {
                var exactOrCompatible =
                    InfModelEvidence.MatchStrength(device, matchingIds) > 0;
                var report = InfPlatformPreflight.Assess(
                    text, device, osArchitecture, osMajor, osMinor, osBuild,
                    hasCatalog);
                if (report.Result == InfPlatformPreflight.Verdict.NoHardwareMatch &&
                    exactOrCompatible)
                    continue;
                // A model-only declaration cannot prove the Windows
                // TargetOSVersion selection, compatible ID, or even that
                // Windows would rank this package for the local device.
                // Never compare version numbers for weak model hints.
                var subsysReview = !exactOrCompatible &&
                    InfModelEvidence.IsRevisionlessSubsysReviewMatch(
                        device, subsysReviewIds);
                var modelOnly = !exactOrCompatible && !subsysReview &&
                    InfModelEvidence.IsPublicModelOnlyReviewMatch(
                        device, modelReviewIds);
                if (!exactOrCompatible && !modelOnly && !subsysReview)
                    continue;

                var package = ReadPackageVersion(report.DriverVersion);
                var comparison = modelOnly || subsysReview
                    ? VersionRelation.Unavailable
                    : CompareVersions(device.DriverVersion, package);
                var finding = new Finding(
                    TrimUntrusted(device.Name, 90),
                    TrimUntrusted(inf.Path.Split('/').Last(), 90),
                    TrimUntrusted(device.DriverVersion, 50),
                    package ?? "non dichiarata",
                    modelOnly || subsysReview ? 0 : report.HardwareMatchStrength,
                    modelOnly || subsysReview
                        ? InfPlatformPreflight.Verdict.ManualReviewRequired
                        : report.Result,
                    comparison)
                {
                    DeviceOrdinal = inventoryOrdinals[device.DeviceId],
                    IsSubsysRevisionReview = subsysReview
                };

                var key = device.DeviceId;
                if (candidates.TryGetValue(key, out var previous))
                {
                    if (IsPreferred(finding, previous.Finding))
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
        var modelOnlyCount = candidates.Values.Count(row =>
            row.Finding.IsModelOnlyReview);
        if (modelOnlyCount > 0)
            notes.Add($"{modelOnlyCount} dispositivi con SOLO modello pubblico " +
                "INF o SUBSYS senza REV: compatibilità, Windows e versione " +
                "NON verificati; NON sono aggiornamenti approvati né installabili.");
        var subsysCount = candidates.Values.Count(row =>
            row.Finding.IsSubsysRevisionReview);
        if (subsysCount > 0)
            notes.Add($"{subsysCount} indizi con SUBSYS OEM identico " +
                "ma REV assente nell'INF: sola revisione, non un match " +
                "PnP confermato da Windows.");

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
            omitted)
        {
            MatchedDeviceOrdinals = candidates.Keys
                .Select(id => inventoryOrdinals[id])
                .OrderBy(index => index)
                .ToArray(),
            PnpMatchedDeviceOrdinals = candidates.Values
                .Where(row => row.Finding.HardwareMatchStrength > 0)
                .Select(row => row.Finding.DeviceOrdinal)
                .Distinct().OrderBy(index => index).ToArray()
        };
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
        // Hardware match strength outranks ALL review-only evidence and
        // version hints. Even an unversioned compatible match must not be
        // hidden by a numerically higher but unrelated generic-model INF.
        (finding.HardwareMatchStrength switch { 2 => 100, 1 => 50, _ => 0 }) +
        (finding.IsSubsysRevisionReview ? 20 : 0) +
        (finding.PlatformVerdict == InfPlatformPreflight.Verdict.ModelSectionPotentialMatch ? 10 : 0) +
        (finding.VersionComparison switch
        {
            VersionRelation.NewerHint => 3,
            VersionRelation.Equal => 2,
            VersionRelation.OlderHint => 1,
            _ => 0
        });

    // An OEM folder/ZIP can contain multiple INF versions for the SAME PnP
    // device. Never depend on archive order for which diagnostic row wins.
    // Match and OS evidence outrank version numbers. Numerical comparison
    // is a presentation hint only, NOT Windows SetupAPI rank or permission
    // to install the driver.
    private static bool IsPreferred(Finding candidate, Finding existing)
    {
        var priority = Priority(candidate).CompareTo(Priority(existing));
        if (priority != 0)
            return priority > 0;

        var candidateParsed = TryReadFourPartVersion(candidate.PackageVersion, out var candidateParts);
        var existingParsed = TryReadFourPartVersion(existing.PackageVersion, out var existingParts);
        if (candidateParsed && existingParsed)
        {
            for (var i = 0; i < 4; i++)
            {
                if (candidateParts[i] != existingParts[i])
                    return candidateParts[i] > existingParts[i];
            }
        }
        else if (candidateParsed != existingParsed)
        {
            // Prefer a declared, syntactically comparable version over an
            // unknown string. This alone NEVER makes IsPotentialNewer true.
            return candidateParsed;
        }
        else
        {
            // Different non-numeric labels get a deterministic order too.
            var labelOrder = StringComparer.OrdinalIgnoreCase.Compare(
                candidate.PackageVersion, existing.PackageVersion);
            if (labelOrder != 0)
                return labelOrder < 0;
        }

        var infOrder = StringComparer.OrdinalIgnoreCase.Compare(
            candidate.InfName, existing.InfName);
        return infOrder != 0
            ? infOrder < 0
            : StringComparer.Ordinal.Compare(candidate.InfName, existing.InfName) < 0;
    }

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
        if (!TryReadFourPartVersion(installed, out var current) ||
            !TryReadFourPartVersion(package, out var offered))
            return VersionRelation.Unavailable;

        for (var i = 0; i < 4; i++)
        {
            if (offered[i] > current[i])
                return VersionRelation.NewerHint;
            if (offered[i] < current[i])
                return VersionRelation.OlderHint;
        }
        return VersionRelation.Equal;
    }

    // The same strict four-part number rule is used for a version hint and
    // to resolve two equally strong INF declarations. No fuzzy semver,
    // string sort, component padding or undocumented OEM version policy.
    private static bool TryReadFourPartVersion(string? value, out int[] parts)
    {
        parts = [];
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var tokens = value.Trim().Split('.');
        if (tokens.Length != 4)
            return false;

        var numbers = new int[4];
        for (var i = 0; i < numbers.Length; i++)
        {
            var token = tokens[i];
            if (token.Length is < 1 or > 6 ||
                !token.All(char.IsAsciiDigit) ||
                !int.TryParse(token, out numbers[i]))
                return false;
        }
        parts = numbers;
        return true;
    }
}
