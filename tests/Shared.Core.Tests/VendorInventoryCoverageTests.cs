using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class VendorInventoryCoverageTests
{
    private static DriverInfo Device(string instance, string name,
        string hardware, string deviceClass = "Net",
        string? version = "30.0") =>
        new(instance, name, "Microsoft", deviceClass, "Microsoft",
            version, null, "oem42.inf", true, true, 0,
            [hardware], []);

    private static DriverUpdateInfo Update(int ordinal, string id) =>
        new($"00000000-0000-4000-8000-{ordinal:D12}", "PRIVATE WUA",
            null, "Net", "Microsoft", null, null, false, null,
            null, id, null, SearchSource: "Windows Update online");

    [Fact]
    public void MoreThanHundredIntelHandoffsDoNotTruncateActualPnpCoverage()
    {
        var intel = Enumerable.Range(0, 180).Select(i =>
            Device($"PRIVATE_INTEL_{i}", "Network Controller",
                $@"PCI\VEN_8086&DEV_{i:X4}"));
        var nvidia = Device("PRIVATE_NVIDIA_SERIAL", "Microsoft Basic Display Adapter",
            @"PCI\VEN_10DE&DEV_28A0", "Display");
        var realtek = Device("PRIVATE_REALTEK_SERIAL", "High Definition Audio Device",
            @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295", "MEDIA");
        var devices = intel.Concat([nvidia, realtek]).ToArray();
        var updates = new[]
        {
            Update(1, @"PCI\VEN_10DE&DEV_28A0"),
            Update(2, @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295")
        };

        Assert.Equal(100, VendorSourceDiscovery.Find(devices, updates).Count);
        var result = VendorInventoryCoverage.Create(devices, updates);
        Assert.True(result.InventoryComplete);
        Assert.True(result.CorrelationsComplete);
        Assert.Equal(182, result.Rows.Sum(row => row.Devices));
        var intelRow = Assert.Single(result.Rows, r => r.Vendor == "Intel");
        Assert.Equal(180, intelRow.Devices);
        Assert.Equal(0, intelRow.DevicesWithWuaMatches);
        Assert.Equal(1, Assert.Single(result.Rows,
            r => r.Vendor == "NVIDIA").DevicesWithWuaMatches);
        Assert.Equal(1, Assert.Single(result.Rows,
            r => r.Vendor == "Realtek").DevicesWithWuaMatches);
        Assert.Contains("inventario PnP completo", result.ScopeNote);
        Assert.DoesNotContain("PRIVATE", string.Join(" ",
            result.Rows.Select(row => row.DisplayLine(true))));
    }

    [Fact]
    public void SameGuidAcrossWindowsSourcesAndDuplicateInstanceCountOnce()
    {
        const string nvidia = @"PCI\VEN_10DE&DEV_28A0";
        const string realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
        var gpu = Device("PRIVATE_GPU", "Generic graphics", nvidia, "Display");
        var codec = Device("PRIVATE_CODEC", "Audio device", realtek, "MEDIA");
        var first = Update(5, nvidia);
        var later = first with { HardwareId = realtek,
            SearchSource = "Windows configurato" };
        var result = VendorInventoryCoverage.Create([gpu, gpu, codec],
            [first, later]);

        Assert.Equal(2, result.Rows.Sum(row => row.Devices));
        Assert.Equal(2, result.Rows.Sum(row => row.DevicesWithWuaMatches));
        Assert.Equal(2, result.Rows.Sum(row => row.WuaCorrelations));
        Assert.Equal(1, Assert.Single(result.Rows,
            row => row.Vendor == "NVIDIA").WuaCorrelations);
        Assert.Equal(1, Assert.Single(result.Rows,
            row => row.Vendor == "Realtek").WuaCorrelations);
        Assert.DoesNotContain(result.Rows, row =>
            row.DisplayLine(true).Contains("PRIVATE"));
    }

    [Fact]
    public void WuaBudgetLimitDoesNotPretendToReportZeroDriverMatches()
    {
        var nvidia = Device("PRIVATE_GPU", "GPU",
            @"PCI\VEN_10DE&DEV_28A0", "Display");
        var huge = Enumerable.Range(1,
            VendorInventoryCoverage.MaximumDriverUpdates + 1)
            .Select(i => Update(i, nvidia.HardwareIds[0])).ToArray();
        var result = VendorInventoryCoverage.Create([nvidia], huge);
        Assert.True(result.InventoryComplete);
        Assert.False(result.CorrelationsComplete);
        var row = Assert.Single(result.Rows);
        var line = row.DisplayLine(true);
        Assert.Contains("non valutabili", line);
        Assert.DoesNotContain("0 con candidati", line);
        Assert.Contains("non valutabili", result.ScopeNote);
        Assert.DoesNotContain("0 con candidati", row.DisplayLine(false));
    }

    [Fact]
    public void MoreThanDeviceBudgetIsExplicitlyPartialNotCompleteAbsence()
    {
        var devices = Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDevices + 1)
            .Select(i => Device($"PRIVATE_INTEL_{i}", "Network Controller",
                $@"PCI\VEN_8086&DEV_{i:X4}")).ToArray();
        var result = VendorInventoryCoverage.Create(devices, []);
        Assert.False(result.InventoryComplete);
        Assert.Contains("parziali", result.ScopeNote);
        var row = Assert.Single(result.Rows);
        Assert.Equal(VendorInventoryCoverage.MaximumDevices, row.Devices);
        Assert.Contains("inventario parziale", row.DisplayLine(true));
    }

    [Fact]
    public void CopiedFullInventoryVendorCoverageDoesNotLeakIdentity()
    {
        var device = Device(@"PRIVATE_DEVICE_SERIAL_9087", "PRIVATE PC",
            @"PCI\VEN_10DE&DEV_28A0", "Display", version: null);
        var report = VendorInventoryCoverage.Create([device],
            [Update(1, device.HardwareIds[0])]);
        var copied = ScanCoverageText.Create(null, report.Rows,
            1, 0, 0, [device], inventoryComplete: true,
            vendorCoverage: report);
        Assert.Contains("NVIDIA: 1 dispositivi", copied);
        Assert.Contains("1 versioni locali mancanti", copied);
        Assert.DoesNotContain("PRIVATE", copied);
        Assert.DoesNotContain("SERIAL", copied);
        Assert.DoesNotContain("VEN_10DE", copied);
        Assert.DoesNotContain("00000000-0000-4000", copied);
    }
}
