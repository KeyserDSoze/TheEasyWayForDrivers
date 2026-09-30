namespace TheEasyWayForDrivers.Core.Models;

public sealed record OemProviderStatus(
    string ProviderId,
    string DisplayName,
    int DetectedDeviceCount,
    bool IsCompanionInstalled,
    string? CompanionVersion,
    string IntegrationMode,
    string OfficialSupportUrl,
    string Message)
{
    public bool IsApplicable => DetectedDeviceCount > 0;

    public string CompanionStatus =>
        IsCompanionInstalled
            ? string.IsNullOrWhiteSpace(CompanionVersion)
                ? "Installato"
                : $"Installato · v{CompanionVersion}"
            : "Non installato";
}
