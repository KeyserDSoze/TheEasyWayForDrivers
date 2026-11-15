namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// #480: diagnostic messages shown by Desktop in MessageBox/status.
/// Exceptions from the IPC service, filesystem, COM or WMI may embed
/// usernames, paths, hostnames, serial numbers or device instance IDs.
/// Render only fixed messages and never propagate exception text.
/// </summary>
public static class DesktopFailureText
{
    public const string ConnectionInterrupted =
        "Comunicazione con il servizio interrotta. Controlla che OmegaDrive " +
        "Service sia in esecuzione e riprova. Esito non verificato.";

    public const string SettingsSaveFailed =
        "Impossibile salvare le impostazioni locali. Controlla i permessi " +
        "della cartella dell'applicazione e riprova.";

    public const string SettingsResetFailed =
        "Impossibile ripristinare le impostazioni. Verifica i permessi " +
        "della cartella dell'applicazione e riprova.";

    public const string HistorySaveFailed =
        "Impossibile salvare la cronologia locale. Controlla lo spazio " +
        "disponibile e le autorizzazioni.";

    public const string HistoryReadFailed =
        "Cronologia locale non leggibile. Il file potrebbe essere " +
        "danneggiato o non accessibile; nessuna modifica ai driver.";

    public const string HistoryExportFailed =
        "Esportazione della cronologia non riuscita. Controlla " +
        "percorso e permessi e scegli un'altra cartella.";

    public static string BusyOperation(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            OperationCanceledException or TimeoutException =>
                "Operazione interrotta o scaduta. La verifica dei driver " +
                "non è completa; riprova e controlla lo stato del servizio.",
            UnauthorizedAccessException =>
                "Autorizzazioni insufficienti per completare l'operazione. " +
                "Controlla i permessi e lo stato del servizio.",
            InvalidDataException or System.Text.Json.JsonException =>
                "Risposta o dati non leggibili. Risultato non verificato; " +
                "riprova dopo aver controllato il servizio.",
            _ =>
                "Operazione non completata. Controlla lo stato del servizio " +
                "e riprova. Il risultato non è stato verificato."
        };
    }
}
