using Microsoft.Extensions.Logging;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.ServiceApp.Services;

public sealed class NvidiaOemDriverProvider(
    ILogger<NvidiaOemDriverProvider> logger) : IOemDriverProvider
{
    public const string OfficialSupportUrl =
        "https://www.nvidia.com/en-us/geforce/drivers/";

    public string ProviderId => "nvidia";

    public Task<OemProviderStatus> GetStatusAsync(
        IReadOnlyList<DriverInfo> drivers,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var deviceCount =
            NvidiaHardwareDetector.CountNvidiaDevices(drivers);

        var companion =
            InstalledSoftwareDetector.Find(
                displayName => string.Equals(
                    displayName.Trim(),
                    "NVIDIA App",
                    StringComparison.OrdinalIgnoreCase));

        logger.LogInformation(
            "NVIDIA OEM provider detected {DeviceCount} NVIDIA devices; NVIDIA App installed: {Installed}; version: {Version}.",
            deviceCount,
            companion.IsInstalled,
            companion.Version ?? "unknown");

        var message =
            deviceCount == 0
                ? "Nessun dispositivo NVIDIA rilevato nell'inventario corrente."
                : companion.IsInstalled
                    ? "NVIDIA App è installata. Il pulsante apre il portale driver NVIDIA ufficiale."
                    : "Sono presenti dispositivi NVIDIA. NVIDIA indica NVIDIA App come companion ufficiale per mantenere aggiornati i driver.";

        return Task.FromResult(new OemProviderStatus(
            ProviderId,
            "NVIDIA",
            deviceCount,
            companion.IsInstalled,
            companion.Version,
            "Companion ufficiale",
            OfficialSupportUrl,
            message));
    }
}
