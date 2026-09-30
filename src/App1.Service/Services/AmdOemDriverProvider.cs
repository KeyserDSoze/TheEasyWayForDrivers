using Microsoft.Extensions.Logging;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.ServiceApp.Services;

public sealed class AmdOemDriverProvider(
    ILogger<AmdOemDriverProvider> logger) : IOemDriverProvider
{
    public const string OfficialSupportUrl =
        "https://www.amd.com/en/support/download/drivers.html";

    public string ProviderId => "amd";

    public Task<OemProviderStatus> GetStatusAsync(
        IReadOnlyList<DriverInfo> drivers,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var deviceCount =
            AmdHardwareDetector.CountAmdDevices(drivers);

        var companion =
            InstalledSoftwareDetector.Find(
                displayName =>
                    displayName.Contains(
                        "AMD Software",
                        StringComparison.OrdinalIgnoreCase) ||
                    displayName.Contains(
                        "Radeon Software",
                        StringComparison.OrdinalIgnoreCase));

        logger.LogInformation(
            "AMD OEM provider detected {DeviceCount} AMD devices; AMD Software installed: {Installed}; version: {Version}.",
            deviceCount,
            companion.IsInstalled,
            companion.Version ?? "unknown");

        var message =
            deviceCount == 0
                ? "Nessun dispositivo AMD rilevato nell'inventario corrente."
                : companion.IsInstalled
                    ? "AMD Software è installato. Il pulsante apre il portale driver AMD ufficiale."
                    : "Sono presenti dispositivi AMD. AMD mette a disposizione Auto-Detect/AMD Software Installer per individuare e installare driver compatibili.";

        return Task.FromResult(new OemProviderStatus(
            ProviderId,
            "AMD",
            deviceCount,
            companion.IsInstalled,
            companion.Version,
            "Companion ufficiale",
            OfficialSupportUrl,
            message));
    }
}
