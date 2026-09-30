namespace TheEasyWayForDrivers.Core.Models;

public sealed record DriverInstallResult(
    bool Succeeded,
    bool RebootRequired,
    string Message);
