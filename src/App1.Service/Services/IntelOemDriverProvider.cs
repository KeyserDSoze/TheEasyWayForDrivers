using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.ServiceApp.Services;

public sealed class IntelOemDriverProvider(
    ILogger<IntelOemDriverProvider> logger) : IOemDriverProvider
{
    public const string OfficialSupportUrl =
        "https://www.intel.com/content/www/us/en/support/detect.html";

    public string ProviderId => "intel";

    public Task<OemProviderStatus> GetStatusAsync(
        IReadOnlyList<DriverInfo> drivers,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var deviceCount =
            IntelHardwareDetector.CountIntelDevices(drivers);

        var companion =
            FindInstalledDsa();

        logger.LogInformation(
            "Intel OEM provider detected {DeviceCount} Intel devices; Intel DSA installed: {Installed}; version: {Version}.",
            deviceCount,
            companion.IsInstalled,
            companion.Version ?? "unknown");

        var message =
            deviceCount == 0
                ? "Nessun dispositivo Intel rilevato nell'inventario corrente."
                : companion.IsInstalled
                    ? "Intel Driver & Support Assistant è installato. Il pulsante apre il flusso Intel ufficiale nel browser."
                    : "Sono presenti dispositivi Intel. Per gli aggiornamenti OEM usiamo il flusso ufficiale Intel Driver & Support Assistant, senza API locali non documentate.";

        return Task.FromResult(new OemProviderStatus(
            ProviderId,
            "Intel",
            deviceCount,
            companion.IsInstalled,
            companion.Version,
            "Companion ufficiale",
            OfficialSupportUrl,
            message));
    }

    private static CompanionInfo FindInstalledDsa()
    {
        foreach (var view in new[]
                 {
                     RegistryView.Registry64,
                     RegistryView.Registry32
                 })
        {
            var result = FindInstalledDsa(view);
            if (result.IsInstalled)
            {
                return result;
            }
        }

        return new CompanionInfo(false, null);
    }

    private static CompanionInfo FindInstalledDsa(
        RegistryView view)
    {
        try
        {
            using var localMachine =
                RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    view);

            using var uninstall =
                localMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

            if (uninstall is null)
            {
                return new CompanionInfo(false, null);
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var subKey =
                    uninstall.OpenSubKey(subKeyName);

                var displayName =
                    subKey?.GetValue("DisplayName") as string;

                if (!IsIntelDsaDisplayName(displayName))
                {
                    continue;
                }

                var version =
                    subKey?.GetValue("DisplayVersion") as string;

                return new CompanionInfo(
                    true,
                    string.IsNullOrWhiteSpace(version)
                        ? null
                        : version.Trim());
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (SecurityException)
        {
        }

        return new CompanionInfo(false, null);
    }

    private static bool IsIntelDsaDisplayName(
        string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        return displayName.Contains(
                   "Intel",
                   StringComparison.OrdinalIgnoreCase) &&
               displayName.Contains(
                   "Driver",
                   StringComparison.OrdinalIgnoreCase) &&
               displayName.Contains(
                   "Support Assistant",
                   StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CompanionInfo(
        bool IsInstalled,
        string? Version);
}
