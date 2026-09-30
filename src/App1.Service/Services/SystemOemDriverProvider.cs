using Microsoft.Extensions.Logging;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.ServiceApp.Services;

public sealed class SystemOemDriverProvider(
    SystemOemDetector detector,
    ILogger<SystemOemDriverProvider> logger) : IOemDriverProvider
{
    public string ProviderId => "system-oem";

    public Task<OemProviderStatus> GetStatusAsync(
        IReadOnlyList<DriverInfo> drivers,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var info = detector.Detect();

        if (!info.IsRecognized ||
            info.Descriptor is null)
        {
            return Task.FromResult(new OemProviderStatus(
                ProviderId,
                "PC OEM",
                0,
                null,
                null,
                "Produttore del PC",
                string.Empty,
                string.IsNullOrWhiteSpace(info.RawManufacturer)
                    ? "Produttore del sistema non riconosciuto."
                    : $"Produttore rilevato: {info.RawManufacturer}."));
        }

        var descriptor =
            info.Descriptor;

        bool? installed = null;
        string? version = null;

        if (descriptor.CompanionDetectionSupported)
        {
            var companion =
                InstalledSoftwareDetector.Find(
                    name => IsCompanion(
                        descriptor.ProviderId,
                        name));

            installed = companion.IsInstalled;
            version = companion.Version;
        }

        logger.LogInformation(
            "System OEM detected: {Oem}; model: {Model}; companion installed: {Installed}.",
            descriptor.DisplayName,
            info.Model ?? "unknown",
            installed?.ToString() ?? "not-verified");

        var modelText =
            string.IsNullOrWhiteSpace(info.Model)
                ? string.Empty
                : $" Modello: {info.Model}.";

        var companionText =
            installed is null
                ? $" {descriptor.CompanionName} viene gestito dal flusso ufficiale del produttore."
                : installed.Value
                    ? $" {descriptor.CompanionName} risulta installato."
                    : $" {descriptor.CompanionName} non risulta installato.";

        return Task.FromResult(new OemProviderStatus(
            ProviderId,
            descriptor.DisplayName,
            1,
            installed,
            version,
            "OEM del PC",
            descriptor.OfficialSupportUrl,
            $"Sistema {descriptor.DisplayName} rilevato.{modelText}{companionText}"));
    }

    private static bool IsCompanion(
        string providerId,
        string displayName) =>
        providerId switch
        {
            "dell" =>
                displayName.Contains(
                    "SupportAssist",
                    StringComparison.OrdinalIgnoreCase),

            "lenovo" =>
                displayName.Contains(
                    "Lenovo Vantage",
                    StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains(
                    "Lenovo System Update",
                    StringComparison.OrdinalIgnoreCase),

            "hp" =>
                displayName.Contains(
                    "HP Support Assistant",
                    StringComparison.OrdinalIgnoreCase),

            "acer" =>
                displayName.Contains(
                    "Acer Care Center",
                    StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains(
                    "Acer Control Center",
                    StringComparison.OrdinalIgnoreCase),

            _ => false
        };
}
