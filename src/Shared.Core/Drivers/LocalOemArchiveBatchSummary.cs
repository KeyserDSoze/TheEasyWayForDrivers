namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Bounded, read-only presentation of several independently validated OEM
/// ZIP/CAB comparisons. An association counts one (archive, PnP device)
/// pair, NOT one unique device or one installable update. No archive path,
/// order number or original archive filename enters this report.
/// </summary>
public static class LocalOemArchiveBatchSummary
{
    public const int MaximumArchives = 12;
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
        IReadOnlyList<string> Notes)
    {
        public bool IsInstallable => false;
        public bool IsVerifiedCompatible => false;
        public string Summary =>
            $"{ArchivesExamined} archivi ZIP/CAB OEM letti; " +
            $"{InfExamined} INF ({RejectedInfCount} scartati); " +
            $"{DeviceCount} dispositivi Windows esaminati; " +
            $"{TotalAssociations} associazioni archivio/dispositivo " +
            $"(NON dispositivi unici), {Findings.Count} righe mostrate, " +
            $"{RowsOmitted} non mostrate; " +
            $"{PotentialNewerShown} versioni superiori tra le righe " +
            "mostrate (SOLO indizi). Un dispositivo può comparire " +
            "in più archivi: NON sono aggiornamenti confermati. " +
            "Firma CAT, ranking Windows e compatibilità finale NON verificati.";
    }

    public static Report Combine(
        IReadOnlyList<LocalOemDriverComparison.Report> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count is < 1 or > MaximumArchives)
            throw new InvalidDataException("Numero archivi OEM fuori limite.");

        var deviceCount = reports[0].DevicesExamined;
        if (reports.Any(report => report.DevicesExamined != deviceCount ||
            report.Findings.Count > MaximumRows ||
            report.RejectedInfCount > report.InfExamined))
            throw new InvalidDataException("Report OEM non confrontabili.");

        // Each package's existing finding order already balances vendors.
        // Round-robin across archives makes an NVIDIA pack visible even if
        // another archive has 80 Intel matches. Do NOT deduplicate by the
        // human-readable device name (distinct PnP instances may share it).
        var selected = new List<LocalOemDriverComparison.Finding>(MaximumRows);
        for (var index = 0; selected.Count < MaximumRows; index++)
        {
            bool any = false;
            for (var archiveIndex = 0; archiveIndex < reports.Count; archiveIndex++)
            {
                var items = reports[archiveIndex].Findings;
                if (index >= items.Count)
                    continue;

                any = true;
                var original = items[index];
                // Mask the original archive and INF filenames: these can
                // contain usernames, serials or PCSpecialist order numbers.
                selected.Add(original with
                {
                    InfName = $"Archivio {archiveIndex + 1:D2} · INF {index + 1:D3}"
                });
                if (selected.Count == MaximumRows)
                    break;
            }
            if (!any)
                break;
        }

        var associations = reports.Sum(report => report.TotalMatchedDevices);
        var omitted = Math.Max(0, associations - selected.Count);
        var rejected = reports.Sum(report => report.RejectedInfCount);
        var notes = new List<string>
        {
            "Confronto multi-archivio in sola lettura: ogni archivio " +
            "è verificato separatamente prima di mostrare i risultati.",
            "Le associazioni tra pacchetto e dispositivo NON sono " +
            "dispositivi unici: la stessa GPU può comparire in più archivi.",
            "Numero ordine, percorsi e nomi originali degli archivi " +
            "non vengono riportati."
        };
        if (omitted > 0)
            notes.Add($"{omitted} associazioni oltre il limite di " +
                "80 righe: report non esaustivo, nessuna assenza dimostrata.");
        if (rejected > 0 || reports.Any(r => r.MatchingDevicesOmitted > 0))
            notes.Add("Almeno un report OEM è parziale: non dedurre " +
                "che i driver non mostrati siano assenti.");
        notes.Add("Nessun download o installer eseguito; CAT, " +
            "applicabilità e ranking non certificati.");

        return new Report(
            reports.Count, deviceCount, associations, omitted,
            reports.Sum(report => report.InfExamined), rejected,
            selected.Count(row => row.IsPotentialNewer), selected, notes);
    }
}
