using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Human-facing explanations of the eight fixed diagnostic categories.
/// All counts come from the local Windows inventory or queried WUA passes.
/// Neither a PnP device nor a software title is an installable driver update.
/// </summary>
public sealed record ReferenceCoveragePresentationRow(
    string Component,
    string PnpStatus,
    string WindowsDriverStatus,
    string SoftwareStatus,
    string NextStep)
{
    public bool IsInstallable => false;
}

public static class ReferenceCoveragePresentation
{
    public static IReadOnlyList<ReferenceCoveragePresentationRow> Create(
        IEnumerable<DriverInfo> devices,
        DriverSearchReport? report,
        bool inventoryComplete)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var rows = ReferenceDriverCoverage.Create(
            inventoryComplete ? devices : [], report);

        var driverQueried =
            WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report);
        var driverPartial = report?.IsPartial == true;
        var softwareQueried = report?.RelatedSoftwareSources?.Any(
            source => source.Succeeded) == true;
        var softwarePartial = report?.SoftwareSearchIsPartial == true;

        return rows.Select(row =>
        {
            var pnp = row.SoftwareOnly
                ? "Componente software: non associato a un dispositivo PnP."
                : !inventoryComplete
                    ? "Inventario PnP non verificato."
                    : row.Devices == 0
                        ? "Nessun dispositivo classificato (non prova assenza hardware)."
                        : row.Devices == 1
                            ? "1 dispositivo riconosciuto nell'inventario PnP."
                            : $"{row.Devices} dispositivi riconosciuti nell'inventario PnP.";

            var driver = row.SoftwareOnly
                ? "Non è un candidato driver WUA Type=Driver."
                : !driverQueried
                    ? "Ricerca driver Windows non verificata."
                    : $"{row.WindowsDriverCandidates} candidati driver Windows correlati per ID" +
                      (driverPartial
                          ? " · ricerca parziale, altre fonti potrebbero rispondere."
                          : " · solo fonti Windows consultate.") +
                      (row.Devices > 0 && row.WuaAssociationsIncomplete
                          ? row.WindowsDriverCandidates == 0
                              ? " · ID WUA incompleti: 0 match osservati, assenza non verificabile."
                              : " · ID WUA incompleti: ulteriori associazioni possibili."
                          : "");

            var software = !softwareQueried
                ? "Ricerca componenti Software WUA non verificata."
                : $"{row.SoftwareTitleHints} titoli Software WUA (solo indizi, senza matching PnP)" +
                  (softwarePartial ? " · ricerca parziale." : " · fonti consultate.");

            var next = !inventoryComplete
                ? "Ripetere l'inventario Windows; nessuna disponibilità OEM deducibile."
                : row.SoftwareOnly
                    ? row.SoftwareTitleHints > 0
                        ? "Possibile componente software: verifica dal produttore, NON installabile da OmegaDrive."
                        : "Verifica Intel/Killer con la fonte ufficiale, senza dedurre aggiornamenti."
                    : row.WindowsDriverCandidates > 0
                        ? "Candidato WUA associato per ID: verificare compatibilità e versione prima di installare."
                        : row.WindowsModelReviewHints > 0
                            ? "Indizi WUA solo per modello/sottosistema senza revisione: " +
                              "verificare compatibilità Windows e versione; non installabili da qui."
                        : row.Devices > 0 && row.WuaAssociationsIncomplete
                            ? "Hardware riconosciuto: alcune offerte WUA hanno ID dispositivo incompleti; " +
                              "controllare anche le fonti ufficiali, senza dedurre assenza di aggiornamenti."
                        : row.Devices > 0
                            ? "Hardware riconosciuto: verificare sul sito o companion ufficiale del produttore."
                            : "Nessun dispositivo classificato: verificare nome, classe e Hardware ID localmente.";

            return new ReferenceCoveragePresentationRow(
                row.Component, pnp, driver, software, next);
        }).ToArray();
    }
}
