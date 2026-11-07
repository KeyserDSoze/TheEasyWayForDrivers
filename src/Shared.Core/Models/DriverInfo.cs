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
    IReadOnlyList<string> CompatibleIds,
    bool DriverMetadataQuerySucceeded = true,
    bool PnpIdsRecoveredFromSignedDriver = false,
    // Additive, optional IPC field: local native SetupAPI provided one or
    // more PnP ID categories missing from Win32_PnPEntity.
    bool PnpIdsRecoveredFromSetupApi = false)
{
    // A failed signed-driver WMI lookup must not mark every healthy PnP
    // device as missing a driver. Windows PnP problem codes remain authoritative.
    public bool NeedsAttention =>
        ConfigManagerErrorCode != 0 || (!HasDriver && DriverMetadataQuerySucceeded);
    public bool HasWindowsProblem => ConfigManagerErrorCode != 0;
    public bool IsDriverMissing => ConfigManagerErrorCode == 28;
    public bool IsDriverUnverified => !HasDriver && ConfigManagerErrorCode == 0;
    public string PrimaryHardwareId => HardwareIds.FirstOrDefault() ??
        CompatibleIds.FirstOrDefault() ?? "Non disponibile";
    public string ProblemExplanation => ConfigManagerErrorCode switch
    {
        0 when !DriverMetadataQuerySucceeded =>
            "Inventario PnP disponibile, ma metadati dei driver firmati non verificati; " +
            "la mancanza della versione non prova che il driver sia assente.",
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
        IsDriverMissing
            ? "Driver mancante"
            : HasWindowsProblem
                ? "Errore Windows"
                : IsDriverUnverified
                    ? "Da verificare"
                    : "OK";

    public string StatusDetail => ProblemExplanation;
}
