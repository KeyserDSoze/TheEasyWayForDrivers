using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.ServiceApp.Infrastructure;
using TheEasyWayForDrivers.ServiceApp.Ipc;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// Service IPC integration tests with fake inventory/WUA/OEM providers.
/// No real driver search, download, install, or machine state mutation.
/// </summary>
public sealed class IpcCommandDispatcherTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task OemProviders_RequestFromDesktopShape_ReturnsTypedJson()
    {
        var inventory = new FakeInventory();
        var dispatcher = CreateDispatcher(
            inventory,
            new FakeUpdates(),
            new FakeAppUpdater(),
            new FakeProvider("intel", detectedCount: 3),
            new FakeProvider("nvidia", detectedCount: 0));

        const string desktopJson =
            """{"command":"oem-providers","updateIds":null,"searchMode":null}""";
        var request = JsonSerializer.Deserialize<IpcRequest>(desktopJson, JsonOptions);
        Assert.NotNull(request);

        var result = await dispatcher.DispatchAsync(request, null, CancellationToken.None);
        var responseJson = JsonSerializer.Serialize(result, JsonOptions);
        var statuses = JsonSerializer.Deserialize<OemProviderStatus[]>(responseJson, JsonOptions);

        Assert.NotNull(statuses);
        Assert.Equal(2, statuses.Length);
        Assert.Equal("intel", statuses[0].ProviderId);
        Assert.Equal(3, statuses[0].DetectedDeviceCount);
        Assert.Equal("nvidia", statuses[1].ProviderId);
        Assert.Equal(1, inventory.Calls);
    }

    [Fact]
    public async Task OemProviders_OneProviderFails_OthersStillLoad()
    {
        var dispatcher = CreateDispatcher(
            new FakeInventory(),
            new FakeUpdates(),
            new FakeAppUpdater(),
            new FakeProvider("broken", detectedCount: 1, shouldFail: true),
            new FakeProvider("intel", detectedCount: 2));

        var payload = await dispatcher.DispatchAsync(
            new IpcRequest("oem-providers", null), null, CancellationToken.None);

        var statuses = Assert.IsAssignableFrom<IReadOnlyList<OemProviderStatus>>(payload);
        Assert.Equal(2, statuses.Count);
        Assert.Equal("intel", statuses[0].ProviderId);
        Assert.Contains(statuses, p => p.ProviderId == "broken" && p.IntegrationMode == "Errore provider");
    }

    [Fact]
    public async Task Scan_ReturnsFakeDeviceInventory()
    {
        var drivers = new[]
        {
            new DriverInfo(
                @"PCI\VEN_8086&DEV_1234",
                "Test network adapter",
                "Intel",
                "Net",
                "Test provider",
                "1.0",
                null,
                null,
                true,
                true,
                0,
                [@"PCI\VEN_8086&DEV_1234"],
                [])
        };
        var inventory = new FakeInventory(drivers);
        var dispatcher = CreateDispatcher(inventory, new FakeUpdates(), new FakeAppUpdater());

        var payload = await dispatcher.DispatchAsync(
            new IpcRequest("scan", null), null, CancellationToken.None);

        var reported = Assert.IsAssignableFrom<IReadOnlyList<DriverInfo>>(payload);
        Assert.Single(reported);
        Assert.Equal("Test network adapter", reported[0].Name);
        Assert.Equal(1, inventory.Calls);
    }

    [Theory]
    [InlineData(null, DriverSearchMode.Recommended)]
    [InlineData("Recommended", DriverSearchMode.Recommended)]
    [InlineData("Comprehensive", DriverSearchMode.Comprehensive)]
    [InlineData("comprehensive", DriverSearchMode.Comprehensive)]
    public async Task SearchUpdates_PreservesModeAndReportsProgress(
        string? selectedMode,
        DriverSearchMode expected)
    {
        var updates = new FakeUpdates();
        var progress = new List<OperationProgress>();
        var dispatcher = CreateDispatcher(new FakeInventory(), updates, new FakeAppUpdater());

        var payload = await dispatcher.DispatchAsync(
            new IpcRequest("search-updates", null, selectedMode),
            progress.Add,
            CancellationToken.None);

        Assert.Equal(expected, updates.LastSearchMode);
        Assert.IsAssignableFrom<IReadOnlyList<DriverUpdateInfo>>(payload);
        Assert.Single(progress);
        Assert.Equal("search", progress[0].Stage);
        Assert.Null(updates.InstalledIds);
    }

    [Fact]
    public async Task SearchReport_DesktopContract_PreservesPartialWuaCoverage()
    {
        var updates = new FakeUpdates();
        var dispatcher = CreateDispatcher(new FakeInventory(), updates, new FakeAppUpdater());

        const string desktopJson =
            """{"command":"search-report","updateIds":null,"searchMode":"Comprehensive"}""";
        var request = JsonSerializer.Deserialize<IpcRequest>(desktopJson, JsonOptions);
        Assert.NotNull(request);

        var payload = await dispatcher.DispatchAsync(
            request, null, CancellationToken.None);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var report = JsonSerializer.Deserialize<DriverSearchReport>(json, JsonOptions);

        Assert.NotNull(report);
        Assert.Equal(DriverSearchMode.Comprehensive, updates.LastSearchMode);
        Assert.True(report.IsPartial);
        Assert.False(report.AllFailed);
        Assert.Equal(2, report.Sources.Count);
        Assert.Equal("Failed", report.Sources[1].Outcome);
    }

    [Fact]
    public async Task SearchUpdates_InvalidMode_DoesNotCallProvider()
    {
        var updates = new FakeUpdates();
        var dispatcher = CreateDispatcher(new FakeInventory(), updates, new FakeAppUpdater());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            dispatcher.DispatchAsync(
                new IpcRequest("search-updates", null, "UnknownMode"),
                null,
                CancellationToken.None));

        Assert.Null(updates.LastSearchMode);
    }

    [Fact]
    public async Task InstallUpdates_ValidatesAndDeduplicatesIds_WithFakeProviderOnly()
    {
        var updates = new FakeUpdates();
        var dispatcher = CreateDispatcher(new FakeInventory(), updates, new FakeAppUpdater());
        var id = Guid.NewGuid().ToString("D");
        var progress = new List<OperationProgress>();

        var payload = await dispatcher.DispatchAsync(
            new IpcRequest("install-updates", [id, id.ToUpperInvariant()]),
            progress.Add,
            CancellationToken.None);

        var result = Assert.IsType<DriverInstallResult>(payload);
        Assert.True(result.Succeeded);
        Assert.NotNull(updates.InstalledIds);
        Assert.Single(updates.InstalledIds);
        Assert.Single(progress);
        Assert.Equal("mock-install", progress[0].Stage);
    }

    [Fact]
    public async Task InstallUpdates_InvalidId_IsRejectedBeforeAnyInstall()
    {
        var updates = new FakeUpdates();
        var dispatcher = CreateDispatcher(new FakeInventory(), updates, new FakeAppUpdater());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            dispatcher.DispatchAsync(
                new IpcRequest("install-updates", ["not-a-guid"]),
                null,
                CancellationToken.None));

        Assert.Null(updates.InstalledIds);
    }

    [Fact]
    public async Task CheckAppUpdate_NullResponse_IsValidEmptyObjectForDesktop()
    {
        var app = new FakeAppUpdater();
        var dispatcher = CreateDispatcher(new FakeInventory(), new FakeUpdates(), app);

        var payload = await dispatcher.DispatchAsync(
            new IpcRequest("check-app-update", null), null, CancellationToken.None);

        Assert.True(app.Called);
        Assert.Equal("{}", JsonSerializer.Serialize(payload, JsonOptions));
    }

    [Fact]
    public async Task Diagnostics_RespondsWithoutAccessingDrivers()
    {
        var inventory = new FakeInventory();
        var dispatcher = CreateDispatcher(inventory, new FakeUpdates(), new FakeAppUpdater());

        var payload = await dispatcher.DispatchAsync(
            new IpcRequest("diagnostics", null), null, CancellationToken.None);

        var info = Assert.IsType<ServiceDiagnosticsInfo>(payload);
        Assert.True(info.ProcessId > 0);
        Assert.Equal(0, inventory.Calls);
    }

    [Fact]
    public async Task UnknownCommand_ReportsFailure()
    {
        var dispatcher = CreateDispatcher(new FakeInventory(), new FakeUpdates(), new FakeAppUpdater());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(
                new IpcRequest("unknown", null), null, CancellationToken.None));

        Assert.Contains("Unknown command", error.Message);
    }

    [Fact]
    public async Task CancelledRequest_NeverCallsInventory()
    {
        var inventory = new FakeInventory();
        var dispatcher = CreateDispatcher(inventory, new FakeUpdates(), new FakeAppUpdater());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            dispatcher.DispatchAsync(
                new IpcRequest("scan", null), null, cancellation.Token));
        Assert.Equal(0, inventory.Calls);
    }

    private static IpcCommandDispatcher CreateDispatcher(
        FakeInventory inventory,
        FakeUpdates updates,
        FakeAppUpdater app,
        params IOemDriverProvider[] providers)
    {
        var coordinator = new OemProviderCoordinator(
            inventory,
            providers,
            NullLogger<OemProviderCoordinator>.Instance);

        return new IpcCommandDispatcher(
            inventory,
            updates,
            coordinator,
            app,
            new ServiceDiagnosticsReader());
    }

    private sealed class FakeInventory(IReadOnlyList<DriverInfo>? devices = null) : IDriverInventory
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<DriverInfo>> GetInstalledDriversAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult<IReadOnlyList<DriverInfo>>(devices ?? []);
        }
    }

    private sealed class FakeUpdates : IDriverSearchCoverageProvider
    {
        public DriverSearchMode? LastSearchMode { get; private set; }
        public IReadOnlyCollection<string>? InstalledIds { get; private set; }

        public Task<IReadOnlyList<DriverUpdateInfo>> SearchAsync(
            DriverSearchMode mode,
            Action<OperationProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSearchMode = mode;
            progress?.Invoke(new OperationProgress("search", 50, "Simulated Windows Update"));
            return Task.FromResult<IReadOnlyList<DriverUpdateInfo>>([]);
        }

        public Task<DriverSearchReport> SearchWithCoverageAsync(
            DriverSearchMode mode,
            Action<OperationProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSearchMode = mode;
            return Task.FromResult(new DriverSearchReport(
                [],
                [
                    new DriverSearchPassStatus("Windows configured", "Completed", 0, "Empty"),
                    new DriverSearchPassStatus("Windows online", "Failed", 0, "Offline")
                ]));
        }

        public Task<DriverInstallResult> InstallAsync(
            IReadOnlyCollection<string> updateIds,
            Action<OperationProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstalledIds = updateIds;
            progress?.Invoke(new OperationProgress("mock-install", 100, "No real driver installed"));
            return Task.FromResult(new DriverInstallResult(true, false, "Mock only"));
        }
    }

    private sealed class FakeAppUpdater : IAppUpdateProvider
    {
        public bool Called { get; private set; }

        public Task<AppUpdateInfo?> CheckAsync(
            Version currentVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Called = true;
            return Task.FromResult<AppUpdateInfo?>(null);
        }
    }

    private sealed class FakeProvider(
        string providerId,
        int detectedCount,
        bool shouldFail = false) : IOemDriverProvider
    {
        public string ProviderId => providerId;

        public Task<OemProviderStatus> GetStatusAsync(
            IReadOnlyList<DriverInfo> drivers,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (shouldFail)
            {
                throw new InvalidOperationException("Simulated OEM provider failure");
            }

            return Task.FromResult(
                new OemProviderStatus(
                    providerId,
                    providerId,
                    detectedCount,
                    false,
                    null,
                    "Supporto ufficiale",
                    "https://example.com/support",
                    "Test only"));
        }
    }
}
