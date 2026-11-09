using TheEasyWayForDrivers.Core.Models;

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

        var wuaWorked = wuaReport is { AllFailed: false };
        var wuaCount = wuaWorked ? wuaReport!.Updates.Count : 0;
        var partialWua = wuaReport is { AllFailed: false, IsPartial: true };
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
