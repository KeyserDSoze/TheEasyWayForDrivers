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
    uint ConfigManagerErrorCode)
{
    public bool NeedsAttention => ConfigManagerErrorCode != 0 || !HasDriver;

    public string Status =>
        !HasDriver
            ? "Driver mancante"
            : ConfigManagerErrorCode != 0
                ? "Errore Windows"
                : "OK";

    public string StatusDetail =>
        !HasDriver
            ? "Nessun driver firmato associato al dispositivo."
            : ConfigManagerErrorCode != 0
                ? $"Windows segnala il codice problema {ConfigManagerErrorCode}."
                : "Il dispositivo non presenta problemi rilevati nell'inventario.";
}
