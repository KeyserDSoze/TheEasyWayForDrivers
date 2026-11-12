using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A duplicated PnP DeviceId cannot represent two recognized hardware
/// vendors. The OEM companion already failed closed; guided Home,
/// handoffs, vendor inventory and the eight-row WUA benchmark must agree.
/// </summary>
public sealed class PnpDuplicateVendorConsistencyTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string IntelKiller =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Realtek =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Qualcomm =
        @"PCI\VEN_17CB&DEV_1103";
    private const string WuaGuid =
        "00000000-0000-4000-8000-000000000383";

    private static DriverInfo Device(
        string deviceId, string hardwareId, string name = "Windows Device",
        string deviceClass = "Unknown") =>
        new(deviceId, name, "Microsoft", deviceClass, "Microsoft",
            "31.0.0.1", null, "PRIVATE_oem42.inf",
            true, true, 0, [hardwareId], []);

    private static DriverUpdateInfo Wua(string id) =>
        new(WuaGuid, "Windows driver", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    private static DriverSearchReport Report(string id) =>
        new([Wua(id)], [
            new DriverSearchPassStatus(
                "Windows Update online", "Completed", 1, "verified")
        ]);

    [Theory]
    [InlineData(Nvidia, Realtek, "Display", "MEDIA")]
    [InlineData(IntelKiller, Qualcomm, "Net", "Net")]
    [InlineData(Nvidia, IntelKiller, "Display", "Net")]
    public void ConflictingRecognizedRowsForOneIdAreDroppedInBothOrders(
        string firstId, string secondId, string firstClass,
        string secondClass)
    {
        var first = Device("PRIVATE_SAME_INSTANCE", firstId,
            "Microsoft Basic Adapter", firstClass);
        var second = Device("private_same_instance", secondId,
            "Windows Device", secondClass);
        var report = Report(firstId);

        foreach (var items in new[] {
            new[] { first, second },
            [second, first]
        })
        {
            Assert.Empty(PnpDistinctTrustedDevices.Create(items));

            var home = HomeVendorCoverageSummary.Create(items, report, true);
            Assert.Equal(3, home.Count);
            Assert.All(home, row =>
            {
                Assert.Contains("0 dispositivi PnP", row.DeviceStatus);
                Assert.Contains("0 candidati WUA per ID", row.WindowsStatus);
                Assert.False(row.HasLocalDevice);
                Assert.False(row.CanInspectLocalDrivers);
                Assert.False(row.IsInstallable);
            });
            foreach (var label in new[] { "NVIDIA", "Intel / Killer", "Realtek" })
                Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                    items, label, inventoryComplete: true));

            var inventory = VendorInventoryCoverage.Create(items, report.Updates);
            Assert.True(inventory.InventoryComplete);
            Assert.True(inventory.CorrelationsComplete);
            Assert.Empty(inventory.Rows);

            var benchmark = ReferenceDriverCoverage.Create(items, report);
            Assert.Equal(8, benchmark.Count);
            Assert.All(benchmark, row =>
            {
                Assert.Equal(0, row.Devices);
                Assert.Equal(0, row.WindowsDriverCandidates);
                Assert.Equal(0, row.WindowsModelReviewHints);
            });
            Assert.Empty(VendorSourceDiscovery.Find(items, report.Updates));

            var copy = ScanCoverageText.Create(
                report, inventory.Rows, items.Length, 0, 0, items,
                inventoryComplete: true, vendorCoverage: inventory);
            Assert.DoesNotContain("PRIVATE_", copy);
            Assert.DoesNotContain(firstId, copy);
            Assert.DoesNotContain(secondId, copy);
            Assert.DoesNotContain(WuaGuid, copy);
        }
    }

    [Fact]
    public void EmptyNameOnConflictingRowStillVetoesSupportHandoff()
    {
        var gpu = Device("PRIVATE_GPU", Nvidia,
            "Microsoft Basic Display Adapter", "Display");
        var hidden = Device("private_gpu", Realtek, "", "MEDIA");
        Assert.NotNull(VendorSourceDiscovery.GetRecognizedSupportVendor(gpu));
        Assert.Empty(VendorSourceDiscovery.Find([gpu, hidden], [Wua(Nvidia)]));
        Assert.Empty(VendorSourceDiscovery.Find([hidden, gpu], [Wua(Nvidia)]));
        Assert.Empty(PnpDistinctTrustedDevices.Create([gpu, hidden]));
    }

    [Fact]
    public void IndividuallyConflictingRowCannotBecomeTrustedByDeduplication()
    {
        var mixed = Device("PRIVATE_AMBIGUOUS", Nvidia, "GPU", "Display")
            with { HardwareIds = [Nvidia, Realtek] };
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Conflicting,
            PnpKnownVendorIdentity.FromDriver(mixed));
        Assert.Empty(PnpDistinctTrustedDevices.Create([mixed, mixed]));
        Assert.Empty(VendorSourceDiscovery.Find([mixed], [Wua(Nvidia)]));
        Assert.Empty(VendorInventoryCoverage.Create([mixed], [Wua(Nvidia)]).Rows);
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [mixed], "NVIDIA", true));
    }

    [Fact]
    public void ConsistentDuplicatesKeepSingleDeviceAndExactWuaCorrelation()
    {
        var gpu = Device("PRIVATE_GPU", Nvidia,
            "Microsoft Basic Display Adapter", "Display");
        var repeated = gpu with
        {
            DeviceId = "private_gpu",
            DriverProvider = "NVIDIA",
            Name = "GeForce RTX 4060"
        };
        var intel = Device("PRIVATE_KILLER", IntelKiller,
            "Killer Wi-Fi 6E", "Net");
        var devices = new[] { gpu, repeated, intel };
        var report = Report(Nvidia);
        var trusted = PnpDistinctTrustedDevices.Create(devices);
        Assert.Equal(2, trusted.Count);
        Assert.Same(gpu, trusted[0]);
        Assert.Same(intel, trusted[1]);

        var home = HomeVendorCoverageSummary.Create(devices, report, true);
        Assert.Contains("1 dispositivo PnP", home[0].DeviceStatus);
        Assert.Contains("1 candidato WUA per ID", home[0].WindowsStatus);
        Assert.Contains("1 dispositivo PnP", home[1].DeviceStatus);
        Assert.Equal(gpu,
            HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                devices, "NVIDIA", true));

        var inventory = VendorInventoryCoverage.Create(devices, report.Updates);
        Assert.Equal(2, inventory.Rows.Sum(row => row.Devices));
        Assert.Equal(1, inventory.Rows.Sum(row => row.DevicesWithWuaMatches));

        var benchmark = ReferenceDriverCoverage.Create(devices, report);
        Assert.Equal(1, benchmark[5].Devices);
        Assert.Equal(1, benchmark[5].WindowsDriverCandidates);
        Assert.Equal(1, benchmark[4].Devices);
        Assert.Equal(0, benchmark[4].WindowsDriverCandidates);

        var handoffs = VendorSourceDiscovery.Find(devices, report.Updates);
        Assert.Equal(2, handoffs.Count);
        Assert.Single(handoffs, h => h.Vendor == "NVIDIA");
        Assert.Single(handoffs, h => h.Vendor == "Killer / Intel");
    }

    [Fact]
    public void InconsistentInstanceCannotHideUnrelatedHealthyDevices()
    {
        var bad = Device("PRIVATE_BAD", Nvidia, "GPU", "Display");
        var badOther = Device("private_bad", Realtek, "Audio", "MEDIA");
        var good = Device("PRIVATE_GOOD", IntelKiller, "Killer Wi-Fi", "Net");
        var kept = PnpDistinctTrustedDevices.Create([bad, badOther, good]);
        Assert.Equal(good, Assert.Single(kept));
        var rows = HomeVendorCoverageSummary.Create(
            [bad, badOther, good], Report(IntelKiller), true);
        Assert.Contains("0 dispositivi PnP", rows[0].DeviceStatus);
        Assert.Contains("1 dispositivo PnP", rows[1].DeviceStatus);
        Assert.Contains("1 candidato WUA per ID", rows[1].WindowsStatus);
    }

    [Fact]
    public void EmptyDeviceIdsCannotBeUsedAsStableDuplicateKeys()
    {
        var invalid = Device("", Nvidia);
        var valid = Device("PRIVATE_GPU", Nvidia);
        var retained = PnpDistinctTrustedDevices.Create([invalid, valid]);
        Assert.Equal(valid, Assert.Single(retained));
    }
}
