using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class HomeVendorCoverageSummaryTests
{
    private static DriverInfo Device(string instance, string title,
        string deviceClass, string id) =>
        new(instance, title, "Microsoft", deviceClass, "Microsoft",
            "31.0.0.1", null, "oem9.inf", true, true, 0, [id], []);

    private static DriverUpdateInfo Update(string guid, string id) =>
        new(guid, "OEM driver from Windows", null, "Display",
            "Microsoft", null, 2048, false, "OEM", null, id, null,
            SearchSource: "Windows Update online");

    private static DriverSearchPassStatus Pass(string outcome) =>
        new("Windows Update online", outcome, 0, "private diagnostics");

    private static readonly DriverInfo Nvidia =
        Device("PRIVATE_GPU", "Microsoft Basic Display Adapter", "Display",
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA");
    private static readonly DriverInfo Killer =
        Device("PRIVATE_NETWORK", "Killer Wi-Fi 6E", "Net",
            @"PCI\VEN_8086&DEV_51F1");
    private static readonly DriverInfo Realtek =
        Device("PRIVATE_AUDIO", "High Definition Audio Device", "MEDIA",
            @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295");

    [Fact]
    public void RecognizesGenericNvidiaKillerRealtekAndDeduplicatesWindowsGuid()
    {
        var id = Guid.NewGuid().ToString("D");
        var report = new DriverSearchReport(
            [Update(id, Nvidia.HardwareIds[0]),
             Update(id.ToUpperInvariant(), Nvidia.HardwareIds[0]),
             Update(Guid.NewGuid().ToString("D"), Killer.HardwareIds[0])],
            [Pass("Completed")]);

        var rows = HomeVendorCoverageSummary.Create(
            [Nvidia, Nvidia with { DeviceId = "private_gpu" }, Killer, Realtek],
            report, true);

        Assert.Equal(["NVIDIA", "Intel / Killer", "Realtek"],
            rows.Select(row => row.Group).ToArray());
        Assert.All(rows, row => Assert.Contains(
            "1 dispositivo PnP", row.DeviceStatus));
        Assert.Contains("1 candidato WUA", rows[0].WindowsStatus);
        Assert.Contains("1 candidato WUA", rows[1].WindowsStatus);
        Assert.Contains("0 candidati WUA", rows[2].WindowsStatus);
        Assert.All(rows, row => Assert.False(row.IsInstallable));
        var text = string.Join(" ", rows.SelectMany(row =>
            new[] { row.Group, row.DeviceStatus, row.WindowsStatus }));
        Assert.DoesNotContain("PRIVATE", text);
        Assert.DoesNotContain(@"PCI\", text);
        Assert.DoesNotContain(@"HDAUDIO\", text);
        Assert.DoesNotContain("OEM driver", text);
    }

    [Fact]
    public void NoInventoryOrFailedSearchNeverMeansZeroDriversAvailable()
    {
        var failed = new DriverSearchReport([], [Pass("Failed")]);
        var noInventory = HomeVendorCoverageSummary.Create(
            [Nvidia], failed, false);
        Assert.All(noInventory, row =>
        {
            Assert.Contains("PnP non verificato", row.DeviceStatus);
            Assert.Contains("non disponibile", row.WindowsStatus);
            Assert.DoesNotContain("0 candidati", row.WindowsStatus);
        });

        var unavailable = HomeVendorCoverageSummary.Create(
            [Nvidia], failed, true);
        Assert.Contains("1 dispositivo", unavailable[0].DeviceStatus);
        Assert.All(unavailable, row =>
            Assert.Contains("non verificata", row.WindowsStatus));
        Assert.DoesNotContain(unavailable,
            row => row.WindowsStatus.Contains("0 candidati"));
    }

    [Fact]
    public void PartialWuaScanClearlyStatesLimitedCoverage()
    {
        var report = new DriverSearchReport([], [
            Pass("Completed"), Pass("Failed")
        ]);
        var rows = HomeVendorCoverageSummary.Create(
            [Nvidia], report, true);

        Assert.Contains("0 candidati WUA", rows[0].WindowsStatus);
        Assert.Contains("parziali", rows[0].WindowsStatus);
        Assert.Contains("0 dispositivi", rows[1].DeviceStatus);
        Assert.Contains("0 dispositivi", rows[2].DeviceStatus);
        Assert.DoesNotContain("aggiornato", rows[0].WindowsStatus);
    }

    [Fact]
    public void TypeSoftwareAndUnrelatedIntelBridgesDoNotInflateDriverCounts()
    {
        var unrelated = Device("PRIVATE_BRIDGE", "Intel Host Bridge",
            "System", @"PCI\VEN_8086&DEV_1234");
        var report = new DriverSearchReport(
            [], [Pass("Completed")],
            [new WuaDriverRelatedSoftware(
                Guid.NewGuid().ToString("D"),
                "Killer Networking Software", "Windows Update online")],
            [Pass("Completed")]);
        var rows = HomeVendorCoverageSummary.Create(
            [unrelated, Killer], report, true);

        Assert.Equal("1 dispositivo PnP classificato", rows[1].DeviceStatus);
        Assert.Contains("0 candidati WUA", rows[1].WindowsStatus);
        Assert.All(rows, row => Assert.False(row.IsInstallable));
    }
}
