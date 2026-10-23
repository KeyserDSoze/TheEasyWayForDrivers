namespace TheEasyWayForDrivers.Core.Models;

/// <summary>
/// Localized overview of a guided read-only PC scan. OEM channels are intentionally
/// reported as support sources, never as confirmed driver updates.
/// </summary>
public sealed record GuidedScanReport(
    string Headline,
    string DeviceSummary,
    string SourceSummary,
    string Caveat)
{
    public static GuidedScanReport Create(
        bool inventoryComplete,
        bool windowsUpdateComplete,
        bool oemComplete,
        int devices,
        int needsAttention,
        int ordinaryCandidates,
        int advancedCandidates,
        int matchedDevices,
        int applicableOemSources,
        string? reportedPcModel,
        IReadOnlyCollection<string> unavailableSteps)
    {
        ArgumentNullException.ThrowIfNull(unavailableSteps);

        var complete = inventoryComplete && windowsUpdateComplete &&
            oemComplete && unavailableSteps.Count == 0;

        var title = complete
            ? "Analisi terminata"
            : "Analisi parziale: alcuni controlli non sono riusciti";

        var modelHint = oemComplete && !string.IsNullOrWhiteSpace(reportedPcModel)
            ? $"PC rilevato: {reportedPcModel.Trim()}. "
            : string.Empty;

        var deviceText = inventoryComplete
            ? $"{modelHint}{devices} dispositivi esaminati, {needsAttention} da controllare."
            : "Non è stato possibile leggere l'inventario dei dispositivi.";

        var wuaText = windowsUpdateComplete
            ? $"Windows Update: {ordinaryCandidates} candidati normali" +
              (advancedCandidates > 0
                  ? $", {advancedCandidates} avanzati/nascosti"
                  : string.Empty) +
              $"; {matchedDevices} dispositivi correlati per ID."
            : "Windows Update: ricerca non riuscita.";

        var oemText = oemComplete
            ? $" Fonti ufficiali OEM applicabili: {applicableOemSources} (canali di supporto, non pacchetti verificati)."
            : " Fonti OEM: verifica non riuscita.";

        var caveat = complete
            ? "I cataloghi OEM esterni non sono tutti interrogabili automaticamente. Un candidato WUA non è prova di compatibilità o installabilità."
            : unavailableSteps.Count > 0
                ? $"Verifiche non completate: {string.Join(", ", unavailableSteps)}. I risultati potrebbero essere incompleti."
                : "Alcuni controlli non sono disponibili. I risultati potrebbero essere incompleti.";

        return new GuidedScanReport(title, deviceText, wuaText + oemText, caveat);
    }
}
