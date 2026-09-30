using Microsoft.Extensions.Logging;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.ServiceApp.Services;

public sealed class OemProviderCoordinator(
    IDriverInventory driverInventory,
    IEnumerable<IOemDriverProvider> providers,
    ILogger<OemProviderCoordinator> logger)
{
    public async Task<IReadOnlyList<OemProviderStatus>> GetStatusesAsync(
        CancellationToken cancellationToken)
    {
        var drivers =
            await driverInventory.GetInstalledDriversAsync(
                cancellationToken);

        var statuses =
            new List<OemProviderStatus>();

        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                statuses.Add(
                    await provider.GetStatusAsync(
                        drivers,
                        cancellationToken));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "OEM provider {ProviderId} status check failed.",
                    provider.ProviderId);

                statuses.Add(new OemProviderStatus(
                    provider.ProviderId,
                    provider.ProviderId,
                    0,
                    false,
                    null,
                    "Errore provider",
                    string.Empty,
                    "Impossibile leggere lo stato del provider OEM."));
            }
        }

        return statuses
            .OrderByDescending(status => status.IsApplicable)
            .ThenBy(
                status => status.DisplayName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
