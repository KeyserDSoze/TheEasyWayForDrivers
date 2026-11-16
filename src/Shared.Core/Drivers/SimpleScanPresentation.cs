using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Plain-language, non-misleading interpretation of the guided scan.
/// Catalog handoffs count as devices with official sources, never updates.
/// </summary>
public sealed record SimpleScanPresentation(
    string Headline,
    string DeviceLine,
    string SourceLine,
    string Warning)
{
    public static SimpleScanPresentation Create(
        DriverSearchReport? wuaReport,
        bool inventoryComplete,
        int inventoriedDevices,
        int problems,
        int oemRecommended,
        IReadOnlyCollection<string> unavailableSteps)
    {
        ArgumentNullException.ThrowIfNull(unavailableSteps);

        // The headline must agree with the visible WUA driver list.
        // Legacy IPC rows from failed passes and duplicate GUID formats
        // never count as separate verified update offers.
        var wuaWorked =
            WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(wuaReport);
        // #469: the Home headline and the main list use the SAME
        // confirmed, GUID-deduplicated Type=Driver records. Count ordinary
        // selectable WUA GUIDs with Update All's canonical policy, never
        // inferred from titles, OEM handoffs or Type=Software hints.
        var confirmedWua = WindowsDriverCandidateMerge
            .ConfirmedDistinctForReport(wuaReport);
        var wuaCount = confirmedWua.Count;
        var ordinaryWua = QuickUpdatePolicy.AllOrdinaryIds(confirmedWua).Count;
        var reviewWua = wuaCount - ordinaryWua;
        var partialWua = wuaWorked && wuaReport?.IsPartial == true;
        var scanPartial = !wuaWorked || partialWua ||
            unavailableSteps.Count > 0 || !inventoryComplete;
        // #512: only GUIDs accepted by Update All's ordinary-WUA policy
        // can contribute to the headline's possible-update count. OEM
        // vendor assessments, BrowseOnly, firmware and advanced WUA rows
        // are valuable REVIEW evidence, not ordinary update offers.
        var oemToReview = Math.Max(0, oemRecommended);

        var wuaHeadline = !wuaWorked
            ? "Ricerca aggiornamenti non completata"
            : wuaCount == 0
                ? scanPartial
                    ? "Analisi parziale: nessun aggiornamento verificato " +
                      "dalle fonti riuscite"
                    : oemToReview > 0
                        ? "Nessun candidato Windows Update dalle fonti consultate"
                        : "Nessun aggiornamento proposto dalle fonti consultate"
                : ordinaryWua == 0
                    ? (scanPartial ? "Risultati parziali: " : "") +
                      (wuaCount == 1
                          ? "1 risultato Windows Update da verificare"
                          : $"{wuaCount} risultati Windows Update da verificare")
                    : (scanPartial ? "Risultati parziali: " : "") +
                      (ordinaryWua == 1
                          ? "1 possibile aggiornamento rilevato"
                          : $"{ordinaryWua} possibili aggiornamenti rilevati") +
                      (reviewWua > 0
                          ? $" · {reviewWua} risultati Windows Update da verificare"
                          : "");

        // An OEM recommendation is not a WUA GUID, is not eligible for
        // Update All and cannot transform a failed WUA query into success.
        var headline = oemToReview == 0
            ? wuaHeadline
            : wuaHeadline + " · " +
              (oemToReview == 1
                  ? "1 segnalazione OEM da verificare"
                  : $"{oemToReview} segnalazioni OEM da verificare");

        var devicesLine = inventoryComplete
            ? $"{Math.Max(0, inventoriedDevices)} dispositivi esaminati · " +
              $"{Math.Max(0, problems)} con problemi Windows."
            : "Inventario dispositivi non disponibile.";

        var wuaLine = wuaWorked
            ? $"{wuaCount} candidati Windows Update" +
              (wuaCount > 0
                  ? $" · {ordinaryWua} ordinari selezionabili · " +
                    $"{reviewWua} da verificare (nessuna installazione " +
                    "senza nuova verifica Windows)"
                  : "") +
              (partialWua
                  ? " (solo fonti riuscite, copertura PARZIALE)"
                  : "")
            : "Windows Update non verificato";

        var sourceLine = $"{wuaLine} · " +
            $"{oemToReview} segnalati dal produttore. " +
            "I dispositivi riconosciuti senza un driver candidato non sono aggiornamenti." +
            (oemToReview > 0
                ? " Le segnalazioni OEM sono informative e NON entrano in Aggiorna tutto."
                : "");

        var catalogUnavailable = unavailableSteps.Contains(
            AutomaticOemCatalogCoverage.UnavailableStep);
        var warning = unavailableSteps.Count > 0 || partialWua ||
            !inventoryComplete
            ? "Alcuni controlli driver non sono riusciti: i risultati possono essere incompleti. " +
              (catalogUnavailable
                  ? "Catalogo automatico Dell/HP non verificato: " +
                    "controlla Fonti e dettagli e, se disponibile, usa " +
                    "Riprova catalogo OEM. I risultati WUA non cambiano. "
                  : "") +
              "Consulta Fonti e dettagli per i servizi interessati."
            : wuaWorked && wuaReport!.SoftwareSearchIsPartial
                ? "Ricerca driver Windows completata; alcuni controlli informativi " +
                  "dei componenti software OEM sono incompleti. " +
                  "Questo non dimostra che i driver OEM siano aggiornati."
                : wuaWorked
                    ? "Non tutti i cataloghi NVIDIA, Intel, AMD e OEM sono interrogabili " +
                      "automaticamente: nessun risultato non significa driver aggiornati."
                    : "La disponibilità dei driver non è stata verificata.";

        return new SimpleScanPresentation(headline, devicesLine,
            sourceLine, warning);
    }
}
