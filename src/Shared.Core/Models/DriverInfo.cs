namespace TheEasyWayForDrivers.Core.Models;

public sealed record DriverInfo(
    string DeviceId,
    string Name,
    string? Manufacturer,
    string? DeviceClass,
    string? DriverProvider,
    string? DriverVersion,
    DateTimeOffset? DriverDate,
    string? InfName,
    bool IsSigned,
    bool HasDriver,
    uint ConfigManagerErrorCode,
    IReadOnlyList<string> HardwareIds,
    IReadOnlyList<string> CompatibleIds)
{
    public bool NeedsAttention => ConfigManagerErrorCode != 0 || !HasDriver;
    public bool HasWindowsProblem => ConfigManagerErrorCode != 0;
    public string PrimaryHardwareId => HardwareIds.FirstOrDefault() ??
        CompatibleIds.FirstOrDefault() ?? "Non disponibile";
    public string ProblemExplanation => ConfigManagerErrorCode switch
    {
        0 when !HasDriver => "Driver firmato non rilevato nell'inventario WMI; verificare in Gestione dispositivi.",
        0 => "Nessun problema segnalato da Windows.",
        10 => "Il dispositivo non può essere avviato (codice 10).",
        22 => "Il dispositivo è disabilitato (codice 22).",
        28 => "Driver del dispositivo non installato (codice 28).",
        31 => "Windows non riesce a caricare il driver necessario (codice 31).",
        39 => "Impossibile caricare il driver del dispositivo (codice 39).",
        43 => "Windows ha arrestato il dispositivo dopo una segnalazione di errore (codice 43).",
        _ => $"Windows segnala il codice problema {ConfigManagerErrorCode}; consultare Gestione dispositivi."
    };

    public string Status =>
        !HasDriver
            ? "Driver mancante"
            : ConfigManagerErrorCode != 0
                ? "Errore Windows"
                : "OK";

    public string StatusDetail => ProblemExplanation;
}
