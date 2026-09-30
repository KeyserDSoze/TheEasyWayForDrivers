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
}
