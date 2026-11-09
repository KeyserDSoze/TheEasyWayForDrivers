namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Joins user-saved PCSpecialist HTML package NAMES to packages physically
/// observed in an explicitly selected local OEM folder. Exact basename
/// equality only; neither presence nor INF correlation authenticates an
/// archive, proves applicability, or authorizes an update.
/// </summary>
public static class PcSpecialistPackageReconciliation
{
    public enum State
    {
        NotObserved,
        UnsupportedFormat,
        ExaminedWithoutVisibleMatch,
        ReviewOnlyInf,
        DeclaredPnpMatch,
        AmbiguousCopies
    }

    // FileName must be a basename (never a path). Only the already
    // sanitized snapshot Package.Name may appear in any UI message.
    public sealed record LocalSource(
        string FileName, bool Examined, int PnpMatches,
        int ReviewOnlyMatches, bool Partial)
    {
        public static LocalSource FromArchive(
            string basename, LocalOemDriverComparison.Report report)
        {
            ArgumentNullException.ThrowIfNull(report);
            return new(basename, true,
                report.Findings.Count(f => f.HardwareMatchStrength > 0),
                report.Findings.Count(f => f.HardwareMatchStrength == 0),
                report.RejectedInfCount > 0 ||
                report.MatchingDevicesOmitted > 0 ||
                report.AdditionalMatchesOmitted > 0 ||
                report.HasUninspectedNestedArchives ||
                report.UninspectedNestedExecutables > 0 ||
                report.UninspectedDeeperArchives > 0 ||
                report.UninspectedProprietaryPackages > 0 ||
                report.InfExamined == 0);
        }

        public static LocalSource Unsupported(string basename) =>
            new(basename, false, 0, 0, true);
    }

    public sealed record Row(
        PcSpecialistPortalSnapshot.Package Package,
        State Status, string Detail)
    {
        public bool IsInstallable => false;
        public bool IsOfficiallyVerified => false;
        public bool IsWindowsApplicable => false;
    }

    public sealed record Report(IReadOnlyList<Row> Rows)
    {
        public int Examined =>
            Rows.Count(r => r.Status is State.ExaminedWithoutVisibleMatch or
                State.ReviewOnlyInf or State.DeclaredPnpMatch);
        public int WithPnPEvidence =>
            Rows.Count(r => r.Status == State.DeclaredPnpMatch);
        public int ReviewOnly =>
            Rows.Count(r => r.Status == State.ReviewOnlyInf);
        public int NotObserved => Rows.Count(r => r.Status == State.NotObserved);
        public int Unsupported =>
            Rows.Count(r => r.Status == State.UnsupportedFormat);
        public int Ambiguous =>
            Rows.Count(r => r.Status == State.AmbiguousCopies);
        public bool IsInstallable => false;
        public bool IsVerifiedOfficial => false;
        public bool IsComplete => false;

        public string Summary =>
            $"Riconciliazione con {Rows.Count} nomi di pacchetti dell'HTML: " +
            $"{Examined} archivi analizzati, {WithPnPEvidence} con " +
            $"dichiarazioni INF per dispositivi PnP, {ReviewOnly} con soli " +
            $"indizi manuali; {Unsupported} formati non supportati, " +
            $"{Ambiguous} nomi duplicati/ambigui, {NotObserved} non osservati " +
            "nella cartella selezionata. Un file non osservato potrebbe " +
            "essere ancora disponibile sul portale. Origine dell'HTML, " +
            "firma CAT, ranking Windows e necessità di aggiornamento " +
            "NON verificati. Nessun file installato o scaricato.";
    }

    public static Report Compare(
        IReadOnlyList<PcSpecialistPortalSnapshot.Package> snapshot,
        IReadOnlyList<LocalSource> localSources)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(localSources);
        if (snapshot.Count > PcSpecialistPortalSnapshot.MaximumDisplayedPackages ||
            localSources.Count > PcSpecialistLocalCatalogPlan.MaximumVisitedFiles)
            throw new InvalidDataException("Limite riconciliazione OEM superato.");

        // An untrusted source name is never printed or fuzzy matched.
        // A local file name duplicated in separate directories is not
        // arbitrarily assigned to a portal package.
        var sources = new Dictionary<string, List<LocalSource>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var source in localSources)
        {
            if (source.FileName.Length is < 1 or > 255 ||
                source.FileName.IndexOfAny(['/', '\\', '\r', '\n', '\0']) >= 0 ||
                source.PnpMatches < 0 || source.ReviewOnlyMatches < 0 ||
                (!source.Examined &&
                 (source.PnpMatches != 0 || source.ReviewOnlyMatches != 0)))
                throw new InvalidDataException(
                    "Dati del confronto locale OEM non validi.");

            if (!sources.TryGetValue(source.FileName, out var entries))
                sources[source.FileName] = entries = [];
            entries.Add(source);
        }

        var rows = new List<Row>(snapshot.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in snapshot)
        {
            if (!PcSpecialistPortalSnapshot.IsSafePackageName(package.Name) ||
                !names.Add(package.Name))
                throw new InvalidDataException(
                    "Nomi dell'elenco HTML non validi.");

            if (!sources.TryGetValue(package.Name, out var matches))
            {
                rows.Add(new(package, State.NotObserved,
                    "Non osservato nella cartella locale selezionata. " +
                    "Non implica assenza del pacchetto dal portale."));
                continue;
            }
            if (matches.Count != 1)
            {
                rows.Add(new(package, State.AmbiguousCopies,
                    "Nome trovato più volte in percorsi diversi: " +
                    "NON attribuito, selezionare un singolo pacchetto."));
                continue;
            }

            var match = matches[0];
            if (!match.Examined)
            {
                rows.Add(new(package, State.UnsupportedFormat,
                    "File presente, ma formato proprietario/non supportato " +
                    "non analizzato; driver e applicabilità sconosciuti."));
                continue;
            }

            var partial = match.Partial
                ? " Analisi parziale: INF scartati o contenuti non esaminati."
                : string.Empty;
            if (match.PnpMatches > 0)
            {
                rows.Add(new(package, State.DeclaredPnpMatch,
                    $"Archivio letto: {match.PnpMatches} dispositivi con " +
                    "Hardware ID/Compatible ID dichiarato negli INF. " +
                    "Compatibilità e versione necessaria NON verificate." +
                    partial));
            }
            else if (match.ReviewOnlyMatches > 0)
            {
                rows.Add(new(package, State.ReviewOnlyInf,
                    $"Archivio letto: {match.ReviewOnlyMatches} indizi " +
                    "di modello pubblico o sottosistema, SOLO revisione " +
                    "manuale, non compatibilità Windows." + partial));
            }
            else
            {
                rows.Add(new(package, State.ExaminedWithoutVisibleMatch,
                    "Archivio letto, nessun match PnP VISIBILE negli " +
                    "INF esaminati; non prova assenza di driver." + partial));
            }
        }

        return new Report(rows);
    }
}
