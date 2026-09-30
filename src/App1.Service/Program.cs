using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Update;
using TheEasyWayForDrivers.ServiceApp;
using TheEasyWayForDrivers.ServiceApp.Ipc;
using TheEasyWayForDrivers.ServiceApp.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "TheEasyWayForDrivers.Service";
});

builder.Services.AddSingleton<IDriverInventory, WmiDriverInventory>();
builder.Services.AddSingleton<IDriverUpdateProvider, WindowsUpdateDriverProvider>();
builder.Services.AddSingleton<IAppUpdateProvider>(_ =>
    new GitHubReleaseUpdateProvider(new HttpClient()));
builder.Services.AddSingleton<NamedPipeServer>();
builder.Services.AddHostedService<DriverServiceWorker>();

await builder.Build().RunAsync();
