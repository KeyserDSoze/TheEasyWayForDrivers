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
        var totalCandidates = wuaCount + Math.Max(0, oemRecommended);

        // A failed WUA pass is NOT an empty catalog. Even when another pass
        // succeeded with zero offers, Home must never headline the result
        // as an exhaustive "no updates" scan.
        var headline = !wuaWorked && oemRecommended == 0
            ? "Ricerca aggiornamenti non completata"
            : scanPartial && totalCandidates == 0
                ? "Analisi parziale: nessun aggiornamento verificato " +
                  "dalle fonti riuscite"
                : totalCandidates == 0
                    ? "Nessun aggiornamento proposto dalle fonti consultate"
                    // If ALL WUA GUIDs are optional/advanced/hidden,
                    // firmware or otherwise ineligible, do not headline
                    // them as ready-to-install driver updates. This is a
                    // review-only state even when every WUA pass succeeded.
                    : wuaCount > 0 && ordinaryWua == 0 && oemRecommended == 0
                        ? (scanPartial ? "Risultati parziali: " : "") +
                          (wuaCount == 1
                              ? "1 risultato Windows Update da verificare"
                              : $"{wuaCount} risultati Windows Update da verificare")
                        : (scanPartial ? "Risultati parziali: " : "") +
                          (totalCandidates == 1
                              ? "1 possibile aggiornamento rilevato"
                              : $"{totalCandidates} possibili aggiornamenti rilevati");

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
            $"{Math.Max(0, oemRecommended)} segnalati dal produttore. " +
            "I dispositivi riconosciuti senza un driver candidato non sono aggiornamenti.";

        var warning = unavailableSteps.Count > 0 || partialWua ||
            !inventoryComplete
            ? "Alcuni controlli driver non sono riusciti: i risultati possono essere incompleti. " +
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
