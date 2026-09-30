using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheEasyWayForDrivers.ServiceApp.Ipc;

namespace TheEasyWayForDrivers.ServiceApp;

public sealed class DriverServiceWorker(
    NamedPipeServer pipeServer,
    ILogger<DriverServiceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TheEasyWayForDrivers service started.");
        await pipeServer.RunAsync(stoppingToken);
    }
}
