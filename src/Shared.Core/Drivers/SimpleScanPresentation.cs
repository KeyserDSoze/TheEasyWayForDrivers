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

        var headline = !wuaWorked && oemRecommended == 0
            ? "Ricerca aggiornamenti non completata"
            : wuaCount + oemRecommended == 0
                ? "Nessun aggiornamento proposto dalle fonti consultate"
                : wuaCount + oemRecommended == 1
                    ? "1 possibile aggiornamento rilevato"
                    : $"{wuaCount + oemRecommended} possibili aggiornamenti rilevati";

        var devicesLine = inventoryComplete
            ? $"{Math.Max(0, inventoriedDevices)} dispositivi esaminati · " +
              $"{Math.Max(0, problems)} con problemi Windows."
            : "Inventario dispositivi non disponibile.";

        var wuaLine = wuaWorked
            ? $"{wuaCount} candidati Windows Update"
            : "Windows Update non verificato";

        var sourceLine = $"{wuaLine} · " +
            $"{Math.Max(0, oemRecommended)} segnalati dal produttore. " +
            "I dispositivi riconosciuti senza un driver candidato non sono aggiornamenti.";

        var partialWua = wuaReport is { AllFailed: false, IsPartial: true };
        var warning = unavailableSteps.Count > 0 || partialWua
            ? "Alcuni controlli non sono riusciti: i risultati possono essere incompleti. " +
              "Consulta Fonti e dettagli per i servizi interessati."
            : wuaWorked
                ? "Non tutti i cataloghi NVIDIA, Intel, AMD e OEM sono interrogabili " +
                  "automaticamente: nessun risultato non significa driver aggiornati."
                : "La disponibilità dei driver non è stata verificata.";

        return new SimpleScanPresentation(headline, devicesLine,
            sourceLine, warning);
    }
}
