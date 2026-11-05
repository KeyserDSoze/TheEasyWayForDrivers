namespace TheEasyWayForDrivers.Core.Models;

public sealed record DriverUpdateInfo(
    string Id,
    string Title,
    string? Description,
    string? DriverClass,
    string? Provider,
    string? Version,
    long? SizeBytes,
    bool IsDownloaded,
    string? Manufacturer,
    string? Model,
    string? HardwareId,
    DateTimeOffset? DriverDate,
    bool IsHidden = false,
    bool IsOptional = false,
    bool IsAdvancedCandidate = false,
    string SearchSource = "Windows Update",
    // Optional, bounded WUA IWindowsDriverUpdate4 entries beyond the default
    // DriverHardwareID. These are device-correlation evidence only, never
    // additional update IDs or installation authorizations.
    IReadOnlyList<string>? AdditionalHardwareIds = null);
