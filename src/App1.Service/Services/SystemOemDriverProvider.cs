using Microsoft.Extensions.Logging;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Drivers;
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
                    : $"Produttore rilevato: {info.RawManufacturer}.",
                info.RawManufacturer,
                info.Model,
                SystemIdentityNormalizer.BuildSupportInstructions(
                    info.RawManufacturer, info.Model)));
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

        var isBoardModel = SystemBoardClassifier.LooksLikeMotherboard(
            info.RawManufacturer, info.Model);

        var modelText =
            string.IsNullOrWhiteSpace(info.Model)
                ? string.Empty
                : isBoardModel
                    ? $" Scheda madre rilevata: {info.Model} (PC assemblato possibile)."
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
            isBoardModel ? "Scheda madre / possibile assemblato" : "OEM del PC",
            descriptor.OfficialSupportUrl,
            $"Sistema {descriptor.DisplayName} rilevato.{modelText}{companionText}",
            descriptor.DisplayName,
            info.Model,
            isBoardModel
                ? $"Il modello «{info.Model}» sembra una scheda madre: verifica il modello completo sul sito ufficiale {descriptor.DisplayName}, insieme a versione Windows e Hardware ID. Non presumere un PC preassemblato."
                : SystemIdentityNormalizer.BuildSupportInstructions(
                    descriptor.DisplayName, info.Model)));
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

            "msi" =>
                displayName.Contains(
                    "MSI Center",
                    StringComparison.OrdinalIgnoreCase),

            "gigabyte" =>
                displayName.Contains(
                    "GIGABYTE Control Center",
                    StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains(
                    "GIGABYTE App Center",
                    StringComparison.OrdinalIgnoreCase),

            _ => false
        };
}
