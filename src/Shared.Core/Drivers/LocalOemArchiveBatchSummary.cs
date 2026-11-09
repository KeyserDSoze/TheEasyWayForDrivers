namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Bounded read-only summary for individually verified OEM ZIP/CAB archives.
/// Correlation uses an ephemeral ordinal of the SAME PnP inventory, not the
/// human-readable device name and never the actual private Device Instance ID.
/// An OEM match is evidence for review, not an installable driver.
/// </summary>
public static class LocalOemArchiveBatchSummary
{
    // 24 bounded sources cover a complete downloaded notebook driver pack
    // without treating its components as independently installable updates.
    public const int MaximumArchives = 24;
    public const int MaximumRows = LocalOemDriverComparison.MaximumDisplayedFindings;
    public const long MaximumTotalArchiveBytes = 4L * 1024 * 1024 * 1024;

    public sealed record Report(
        int ArchivesExamined,
        int DeviceCount,
        int TotalAssociations,
        int RowsOmitted,
        int InfExamined,
        int RejectedInfCount,
        int PotentialNewerShown,
        IReadOnlyList<LocalOemDriverComparison.Finding> Findings,
        IReadOnlyList<string> Notes,
        int? DistinctMatchedDevices = null,
        int? RepeatedAssociations = null)
    {
        public bool IsInstallable => false;
        public bool IsVerifiedCompatible => false;

        // Propagated only from verified bounded child reports; no original
        // archive names or local filesystem paths are ever retained here.
        public int UninspectedNestedExecutables { get; init; }
        public int UninspectedDeeperArchives { get; init; }
        public int UninspectedProprietaryPackages { get; init; }
        public bool IsPartial =>
            RejectedInfCount > 0 || RowsOmitted > 0 ||
            UninspectedNestedExecutables > 0 ||
            UninspectedDeeperArchives > 0 ||
            UninspectedProprietaryPackages > 0 ||
            Notes.Any(note => note.Contains("parzial",
                StringComparison.OrdinalIgnoreCase));

        public string Summary =>
            $"{ArchivesExamined} archivi ZIP/CAB OEM letti; " +
            $"{InfExamined} INF ({RejectedInfCount} scartati); " +
            $"{DeviceCount} dispositivi Windows esaminati; " +
            (DistinctMatchedDevices is int distinct
                ? $"{distinct} dispositivi distinti con evidenze OEM, " +
                  $"{TotalAssociations} associazioni archivio/dispositivo " +
                  $"({RepeatedAssociations ?? 0} associazioni ripetute fra pacchetti); " +
                  $"{Findings.Count} dispositivi mostrati, " +
                  $"{RowsOmitted} dispositivi oltre il limite; "
                : $"{TotalAssociations} associazioni archivio/dispositivo " +
                  "(NON dispositivi unici; numero distinti NON verificabile); " +
                  $"{Findings.Count} righe mostrate, " +
                  $"{RowsOmitted} non mostrate (associazioni); ") +
            $"{PotentialNewerShown} versioni superiori tra le righe " +
            "mostrate (SOLO indizi). Non sono aggiornamenti confermati. " +
            (IsPartial
                ? "COPERTURA PARZIALE: non dedurre assenza di driver. "
                : "") +
            "Firma CAT, ranking Windows e compatibilità finale NON verificati.";
    }

    private sealed record Candidate(
        int ArchiveIndex, int RowIndex,
        LocalOemDriverComparison.Finding Finding);

    public static Report Combine(
        IReadOnlyList<LocalOemDriverComparison.Report> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count is < 1 or > MaximumArchives)
            throw new InvalidDataException("Numero archivi OEM fuori limite.");

        var deviceCount = reports[0].DevicesExamined;
        if (deviceCount is < 0 or > 2048 ||
            reports.Any(report => report.DevicesExamined != deviceCount ||
                report.Findings.Count > MaximumRows ||
                report.MatchingDevicesOmitted < 0 ||
                report.RejectedInfCount < 0 ||
                report.RejectedInfCount > report.InfExamined))
            throw new InvalidDataException("Report OEM non confrontabili.");

        var associations = reports.Sum(report => report.TotalMatchedDevices);
        var rejected = reports.Sum(report => report.RejectedInfCount);
        if (reports.Any(report => report.UninspectedNestedExecutables < 0 ||
            report.UninspectedDeeperArchives < 0 ||
            report.UninspectedProprietaryPackages < 0))
            throw new InvalidDataException(
                "Contatori anonimi OEM non validi.");
        var skippedExe = checked(reports.Sum(report =>
            report.UninspectedNestedExecutables));
        var skippedDepth = checked(reports.Sum(report =>
            report.UninspectedDeeperArchives));
        var skippedPackages = checked(reports.Sum(report =>
            report.UninspectedProprietaryPackages));

        // New comparisons expose the exact set of ephemeral inventory
        // positions, including those not in their 80 visible rows.
        // Older/synthetic reports may not: then do NOT guess distinct count.
        var withOrdinals = reports.Count(report =>
            report.MatchedDeviceOrdinals is not null);
        if (withOrdinals > 0 && withOrdinals != reports.Count)
            throw new InvalidDataException(
                "Evidenze PnP anonime parziali fra archivi OEM.");

        int? distinct = null;
        int? repeated = null;
        IReadOnlyList<LocalOemDriverComparison.Finding> selected;

        if (withOrdinals == reports.Count)
        {
            var union = new HashSet<int>();
            for (var archiveIndex = 0; archiveIndex < reports.Count; archiveIndex++)
            {
                var report = reports[archiveIndex];
                var own = report.MatchedDeviceOrdinals!;
                var local = new HashSet<int>();
                foreach (var ordinal in own)
                {
                    if (ordinal < 0 || ordinal >= deviceCount ||
                        !local.Add(ordinal))
                        throw new InvalidDataException(
                            "Identificativi anonimi PnP non validi nel report OEM.");
                    union.Add(ordinal);
                }
                if (local.Count != report.TotalMatchedDevices ||
                    report.Findings.Any(f => !local.Contains(f.DeviceOrdinal)) ||
                    report.Findings.Select(f => f.DeviceOrdinal).Distinct().Count() !=
                        report.Findings.Count)
                    throw new InvalidDataException(
                        "Risultati OEM non coerenti con l'inventario PnP.");
            }
            distinct = union.Count;
            repeated = associations - distinct.Value;

            var candidates = reports
                .SelectMany((report, index) =>
                    report.Findings.Select((finding, row) =>
                        new Candidate(index, row, finding)))
                .GroupBy(candidate => candidate.Finding.DeviceOrdinal)
                .Select(group => group
                    .OrderByDescending(item => Priority(item.Finding))
                    .ThenByDescending(item => NumericVersion(item.Finding.PackageVersion))
                    .ThenBy(item => item.ArchiveIndex)
                    .ThenBy(item => item.RowIndex)
                    .First())
                .ToArray();

            // Share the 80 row budget across packages *after* selecting one
            // best visible evidence per actual device, so a dozen copies of
            // an NVIDIA GPU never hide an unrelated Killer/Realtek device.
            var groups = candidates
                .GroupBy(item => item.ArchiveIndex)
                .OrderBy(group => group.Key)
                .Select(group => group
                    .OrderByDescending(item => item.Finding.IsPotentialNewer)
                    .ThenByDescending(item => Priority(item.Finding))
                    .ThenBy(item => item.Finding.DeviceName,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Finding.DeviceOrdinal)
                    .ToArray())
                .ToArray();

            var visible = new List<LocalOemDriverComparison.Finding>(MaximumRows);
            for (var depth = 0; visible.Count < MaximumRows; depth++)
            {
                bool any = false;
                foreach (var group in groups)
                {
                    if (depth >= group.Length)
                        continue;
                    any = true;
                    var winner = group[depth];
                    var matchedArchives = reports.Count(report =>
                        report.MatchedDeviceOrdinals!.Contains(
                            winner.Finding.DeviceOrdinal));
                    visible.Add(winner.Finding with
                    {
                        InfName = matchedArchives == 1
                            ? $"Archivio {winner.ArchiveIndex + 1:D2}"
                            : $"Archivio {winner.ArchiveIndex + 1:D2} · " +
                              $"presente in {matchedArchives} archivi"
                    });
                    if (visible.Count == MaximumRows)
                        break;
                }
                if (!any)
                    break;
            }
            selected = visible;
        }
        else
        {
            // Backward compatible fail-closed presentation: without an
            // ordinal, identical display labels are NOT proof of identity.
            // Do not merge by friendly name or falsely claim unique devices.
            var visible = new List<LocalOemDriverComparison.Finding>(MaximumRows);
            for (var depth = 0; visible.Count < MaximumRows; depth++)
            {
                bool any = false;
                for (var i = 0; i < reports.Count; i++)
                {
                    if (depth >= reports[i].Findings.Count)
                        continue;
                    any = true;
                    visible.Add(reports[i].Findings[depth] with
                    {
                        InfName = $"Archivio {i + 1:D2} · INF {depth + 1:D3}"
                    });
                    if (visible.Count == MaximumRows)
                        break;
                }
                if (!any)
                    break;
            }
            selected = visible;
        }

        var omitted = Math.Max(0, (distinct ?? associations) - selected.Count);
        var notes = new List<string>
        {
            "Confronto multi-archivio in sola lettura; " +
            "nessun pacchetto OEM installato.",
            distinct is null
                ? "Le associazioni tra pacchetto e dispositivo NON sono " +
                  "dispositivi unici: la stessa GPU può comparire in più archivi. " +
                  "Dispositivi distinti NON determinabili: i nomi mostrati " +
                  "non sono identità PnP."
                : "Dispositivi distinti correlati tramite un indice PnP " +
                  "anonimo e temporaneo; le associazioni duplicate " +
                  "non occupano più righe.",
            "Numero ordine, Device Instance ID, percorsi e nomi degli INF " +
            "originali non vengono riportati."
        };
        if (omitted > 0)
            notes.Add($"{omitted} risultati oltre il limite di 80 righe: " +
                "report non esaustivo, nessuna assenza driver dimostrata.");
        if (rejected > 0 || reports.Any(r => r.MatchingDevicesOmitted > 0))
            notes.Add("Almeno un archivio è parziale o ha limitato le righe: " +
                "la migliore versione mostrata è scelta fra le evidenze " +
                "disponibili, non fra ogni driver del pacchetto.");
        if (skippedDepth > 0)
            notes.Add($"{skippedDepth} ZIP/CAB oltre la profondità massima " +
                "NON analizzati: copertura parziale, non assenza di driver.");
        if (skippedExe > 0)
            notes.Add($"{skippedExe} EXE annidati non supportati o oltre il " +
                "limite NON analizzati: copertura parziale, " +
                "non assenza di driver.");
        if (skippedPackages > 0)
            notes.Add($"{skippedPackages} MSI/MSIX/7z/RAR/ISO annidati " +
                "NON analizzati: copertura parziale, non assenza di driver.");
        if (reports.Any(r => r.HasUninspectedNestedArchives &&
            r.UninspectedNestedExecutables == 0 &&
            r.UninspectedDeeperArchives == 0 &&
            r.UninspectedProprietaryPackages == 0))
            notes.Add("Almeno un pacchetto OEM non analizzato, causa non " +
                "precisata dal report precedente: copertura parziale.");
        notes.Add("Nessun download o installer eseguito; " +
            "CAT, applicabilità e ranking non certificati.");

        return new Report(reports.Count, deviceCount, associations, omitted,
            reports.Sum(report => report.InfExamined), rejected,
            selected.Count(f => f.IsPotentialNewer), selected, notes,
            distinct, repeated)
        {
            UninspectedNestedExecutables = skippedExe,
            UninspectedDeeperArchives = skippedDepth,
            UninspectedProprietaryPackages = skippedPackages
        };
    }

    // Presentation quality only: NEVER use this to authorize an install.
    private static int Priority(LocalOemDriverComparison.Finding finding) =>
        (finding.PlatformVerdict ==
            InfPlatformPreflight.Verdict.ModelSectionPotentialMatch ? 10 : 0) +
        (finding.HasExactHardwareId ? 5 : 0) +
        (finding.VersionComparison switch
        {
            LocalOemDriverComparison.VersionRelation.NewerHint => 3,
            LocalOemDriverComparison.VersionRelation.Equal => 2,
            LocalOemDriverComparison.VersionRelation.OlderHint => 1,
            _ => 0
        });

    private static Version NumericVersion(string value)
    {
        var fields = value.Split('.');
        if (fields.Length == 4 &&
            fields.All(f => f.Length is >= 1 and <= 6 &&
                f.All(char.IsAsciiDigit)) &&
            Version.TryParse(value, out var parsed) &&
            parsed is not null)
            return parsed;
        return new Version(0, 0, 0, 0);
    }
}
