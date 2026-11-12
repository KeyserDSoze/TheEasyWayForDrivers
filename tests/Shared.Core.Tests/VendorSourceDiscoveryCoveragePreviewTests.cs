using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #411: manual OEM coverage counts must reveal rows hidden by the
/// bounded, vendor-diverse preview. Not evidence of installable updates.
/// </summary>
public sealed class VendorSourceDiscoveryCoveragePreviewTests
{
    private static DriverInfo Device(
        int index, string id, string cls = "Net",
        string name = "Network Controller") =>
        new("PRIVATE_INSTANCE_" + index, name, "Microsoft", cls,
            "Microsoft", "1.0.0.0", null, "oem42.inf",
            true, true, 0, [id], []);

    private static DriverInfo[] Inventory() =>
        Enumerable.Range(1, 140)
            .Select(i => Device(i, $@"PCI\VEN_8086&DEV_{i:X4}"))
            .Concat([
                Device(141, @"PCI\VEN_10DE&DEV_28A0", "Display",
                    "Microsoft Basic Display Adapter"),
                Device(142, @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B",
                    "MEDIA", "High Definition Audio Device"),
                Device(143, @"PCI\VEN_10EC&DEV_8125"),
                Device(144, @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56",
                    "Net", "Killer Wi-Fi AX1675i")
            ]).ToArray();

    [Fact]
    public void DefaultPreviewDisclosesFortyFourHiddenDevicesWithoutPnpNames()
    {
        var data = Inventory();
        var report = VendorSourceDiscovery.FindWithCoverage(data, []);
        var legacy = VendorSourceDiscovery.Find(data, []);

        Assert.Equal(144, report.EligibleDeviceCount);
        Assert.Equal(100, report.Rows.Count);
        Assert.Equal(44, report.OmittedDeviceCount);
        Assert.Equal(legacy.Select(x => x.Vendor),
            report.Rows.Select(x => x.Vendor));
        Assert.Contains(report.Rows, x => x.Vendor == "NVIDIA");
        Assert.Contains(report.Rows, x => x.Vendor == "Realtek");
        Assert.Contains(report.Rows, x => x.Vendor == "Killer / Intel");

        var compact = VendorManualHandoffSummary.Create(report.Rows);
        Assert.Equal(100, compact.Sum(x => x.GroupedManualDevices));
        Assert.All(compact, x => Assert.False(x.IsInstallable));
        Assert.Contains("PARZIALE", report.CoverageNotice);
        Assert.Contains("100 dispositivi mostrati", report.CoverageNotice);
        Assert.Contains("44 non visualizzati", report.CoverageNotice);
        Assert.Contains("Non sono aggiornamenti OEM verificati",
            report.CoverageNotice);
        Assert.DoesNotContain("PRIVATE", report.CoverageNotice);
        Assert.DoesNotContain("VEN_", report.CoverageNotice);
    }

    [Theory]
    [InlineData(0, 144)]
    [InlineData(1, 143)]
    [InlineData(30, 114)]
    [InlineData(100, 44)]
    [InlineData(150, 0)]
    [InlineData(1000, 0)]
    public void ClampedBudgetsKeepLegacySelectionAndCount(int budget, int omitted)
    {
        var inventory = Inventory();
        var report = VendorSourceDiscovery.FindWithCoverage(
            inventory, [], budget);
        Assert.Equal(144, report.EligibleDeviceCount);
        Assert.Equal(omitted, report.OmittedDeviceCount);
        Assert.Equal(144 - omitted, report.Rows.Count);
        Assert.Equal(report.Rows.Count,
            VendorSourceDiscovery.Find(inventory, [], budget).Count);
        Assert.Equal(omitted > 0,
            report.CoverageNotice.Contains("PARZIALE"));
    }

    [Fact]
    public void NoOmissionOnShortOrEmptyScan()
    {
        var shortReport = VendorSourceDiscovery.FindWithCoverage(
            [Device(1, @"PCI\VEN_10DE&DEV_28A0", "Display",
                "Microsoft Basic Display Adapter")], []);
        Assert.Single(shortReport.Rows);
        Assert.Equal(0, shortReport.OmittedDeviceCount);
        Assert.Equal(string.Empty, shortReport.CoverageNotice);

        var emptyReport = VendorSourceDiscovery.FindWithCoverage([], []);
        Assert.Empty(emptyReport.Rows);
        Assert.Equal(0, emptyReport.EligibleDeviceCount);
        Assert.Equal(0, emptyReport.OmittedDeviceCount);
        Assert.Empty(emptyReport.CoverageNotice);
    }

    [Fact]
    public void ContradictoryDuplicatePnpInstanceNeverInflatesCoverage()
    {
        var nvidia = Device(1, @"PCI\VEN_10DE&DEV_28A0",
            "Display", "NVIDIA GPU");
        var intel = nvidia with { HardwareIds = [@"PCI\VEN_8086&DEV_46A6"] };
        var realtek = Device(2, @"PCI\VEN_10EC&DEV_8125");
        var report = VendorSourceDiscovery.FindWithCoverage(
            [nvidia, intel, realtek], [], 1);
        Assert.Equal(1, report.EligibleDeviceCount);
        Assert.Equal(0, report.OmittedDeviceCount);
        Assert.Equal("Realtek", Assert.Single(report.Rows).Vendor);
        Assert.Empty(report.CoverageNotice);
    }

    [Fact]
    public void NullInputIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            VendorSourceDiscovery.FindWithCoverage(null!, []));
        Assert.Throws<ArgumentNullException>(() =>
            VendorSourceDiscovery.FindWithCoverage([], null!));
    }
}
