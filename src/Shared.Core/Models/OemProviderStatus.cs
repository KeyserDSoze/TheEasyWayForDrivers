namespace TheEasyWayForDrivers.Core.Models;

public sealed record OemProviderStatus(
    string ProviderId,
    string DisplayName,
    int DetectedDeviceCount,
    bool? IsCompanionInstalled,
    string? CompanionVersion,
    string IntegrationMode,
    string OfficialSupportUrl,
    string Message,
    string? SystemManufacturer = null,
    string? SystemModel = null,
    string? SupportInstructions = null)
{
    public bool IsApplicable => DetectedDeviceCount > 0;

    public string SystemIdentity =>
        string.IsNullOrWhiteSpace(SystemModel)
            ? "Modello non identificato"
            : SystemModel;

    public string CompanionStatus =>
        IsCompanionInstalled is null
            ? "Non verificato"
            : IsCompanionInstalled.Value
                ? string.IsNullOrWhiteSpace(CompanionVersion)
                    ? "Installato"
                    : $"Installato · v{CompanionVersion}"
                : "Non installato";
}
