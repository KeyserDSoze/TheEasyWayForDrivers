using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Copy for a genuinely empty guided results list. A failed or partial search
/// is never presented as proof that Windows has no driver candidates.
/// </summary>
public static class CompactResultsEmptyMessage
{
    public static string Create(
        int totalCandidates, DriverSearchReport? report, string? searchText = null)
    {
        if (totalCandidates > 0 && !string.IsNullOrWhiteSpace(searchText))
            return "Nessun driver corrisponde alla ricerca con il filtro corrente. " +
                   "Modifica il testo o seleziona «Tutti i risultati».";

        if (totalCandidates > 0)
            return "Nessun risultato con questo filtro. " +
                   "Seleziona «Tutti i risultati» per vedere anche i driver avanzati.";

        if (!WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report))
            return "Non è stato possibile verificare Windows Update. " +
                   "Riprova l'analisi e consulta i dettagli delle fonti.";

        if (report.IsPartial)
            return "Nessun aggiornamento dalle fonti che hanno risposto. " +
                   "La ricerca è incompleta: altre fonti potrebbero proporre driver.";

        return "Nessun aggiornamento proposto dai servizi Windows consultati. " +
               "Questo non significa che tutti i driver OEM siano aggiornati.";
    }
}
