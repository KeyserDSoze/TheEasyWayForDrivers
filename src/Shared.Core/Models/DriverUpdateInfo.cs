namespace TheEasyWayForDrivers.Core.Models;

public sealed record DriverUpdateInfo(
    string Id,
    string Title,
    string? Description,
    string? DriverClass,
    string? Provider,
    string? Version,
    long? SizeBytes,
    bool IsDownloaded);
