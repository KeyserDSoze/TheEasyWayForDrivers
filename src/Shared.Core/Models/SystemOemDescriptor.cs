namespace TheEasyWayForDrivers.Core.Models;

public sealed record SystemOemDescriptor(
    string ProviderId,
    string DisplayName,
    string OfficialSupportUrl,
    string? CompanionName,
    bool CompanionDetectionSupported);
