using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Provides guidance only; it never asserts that an unverified OEM driver exists.
/// </summary>
public static class DriverRemediationAdvisor
{
    public static string GetNextAction(
        DriverInfo driver,
        bool hasMatchingWindowsUpdate,
        bool hasSearchedWindowsUpdate,
        string recommendedSource)
    {
        ArgumentNullException.ThrowIfNull(driver);

        if (driver.ConfigManagerErrorCode == 22)
        {
            return "Il dispositivo risulta disabilitato: controlla prima Gestione dispositivi. Non installare driver senza verificare il problema.";
        }

        if (hasMatchingWindowsUpdate)
        {
            return "Windows Update ha restituito un driver con Hardware ID corrispondente. Verifica versione e provider prima di confermare l'installazione.";
        }

        if (driver.IsDriverUnverified)
        {
            return "Windows non segnala errori per questo dispositivo, ma i metadati del driver non risultano nell'inventario WMI. Controlla Gestione dispositivi prima di considerarlo mancante o installare un driver.";
        }

        if (!driver.NeedsAttention)
        {
            return "Nessun problema del dispositivo rilevato nell'inventario. Gli aggiornamenti facoltativi si consultano nella relativa sezione.";
        }

        if (!hasSearchedWindowsUpdate)
        {
            return "Esegui una ricerca Windows Update per cercare driver compatibili. Se necessario, passa alla ricerca approfondita.";
        }

        if (driver.HardwareIds.Count == 0 && driver.CompatibleIds.Count == 0)
        {
            return "Nessun Hardware ID disponibile: identifica il modello del PC e consulta il supporto ufficiale del produttore. Non è possibile confermare un driver compatibile.";
        }

        return $"Nessun driver corrispondente rilevato in questa ricerca Windows Update. Consulta le fonti ufficiali ({recommendedSource}) usando Hardware ID e modello del PC; la disponibilità OEM non è stata verificata.";
    }
}
