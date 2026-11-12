using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A vendor-specific primary Hardware ID from an unsupported manufacturer
/// cannot borrow a different known vendor from a secondary Compatible ID.
/// These are read-only source hints, never available driver packages.
/// </summary>
public sealed class PnpUnknownPrimaryVendorConflictTests
{
    private static DriverInfo Device(
        IReadOnlyList<string> hardwareIds,
        IReadOnlyList<string> compatibleIds,
        string deviceClass = "Unknown",
        string name = "NVIDIA Intel Realtek wireless controller") =>
        new("PRIVATE_INSTANCE_SERIAL", name, "NVIDIA", deviceClass,
            "NVIDIA", "1.0", null, "PRIVATE_OEM.inf",
            true, true, 0, hardwareIds, compatibleIds);

    [Theory]
    [InlineData(@"USB\VID_9999&PID_0001", @"USB\VID_0955&PID_7019", "USB")]
    [InlineData(@"PCI\VEN_9999&DEV_0001", @"PCI\VEN_10DE&DEV_28A0", "Display")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_9999&DEV_0001", @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B", "MEDIA")]
    [InlineData(@"SWC\VEN_9999&DEV_0001", @"SWC\VEN_8086&DEV_51F1", "SoftwareComponent")]
    [InlineData(@"USB\VID_9999&PID_0001", @"USB\VID_0BDA&PID_1234", "Bluetooth")]
    [InlineData(@"USB\VID_9999&PID_0001", @"USB\VID_0CF3&PID_E300", "Bluetooth")]
    [InlineData(@"HID\VID_9999&PID_0001", @"HID\VID_046D&PID_C534", "HIDClass")]
    [InlineData(@"HID\VID_9999&PID_0001", @"HID\VID_1532&PID_0100", "HIDClass")]
    [InlineData(@"SNDW\CLASS_00&MAN_0242&PART_0711", @"SNDW\CLASS_00&MAN_025D&PART_0711", "MEDIA")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_0242&FUNC_0711", @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711", "MEDIA")]
    public void UnknownConcretePrimaryVendorVetoesForeignKnownCompatible(
        string primary, string compatible, string deviceClass)
    {
        var device = Device([primary], [compatible], deviceClass);
        Assert.Null(PnpKnownVendorIdentity.FromId(primary));
        Assert.NotNull(PnpKnownVendorIdentity.FromId(compatible));
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Conflicting,
            PnpKnownVendorIdentity.FromDriver(device));

        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Empty(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("Windows / OEM", DriverSourceAdvisor.GetSource(device, []));
        Assert.Equal(0, NvidiaHardwareDetector.CountNvidiaDevices([device]));
        Assert.Equal(0, IntelHardwareDetector.CountIntelDevices([device]));
        Assert.Equal(0, AmdHardwareDetector.CountAmdDevices([device]));

        var home = HomeVendorCoverageSummary.Create([device], null, true);
        Assert.All(home, row =>
        {
            Assert.Equal("0 dispositivi PnP classificati", row.DeviceStatus);
            Assert.False(row.CanInspectLocalDrivers);
            Assert.False(row.IsInstallable);
        });
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [device], "NVIDIA", true));
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [device], "Intel / Killer", true));
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [device], "Realtek", true));

        var coverage = VendorInventoryCoverage.Create([device], []);
        Assert.Empty(coverage.Rows);
        var text = ScanCoverageText.Create(null, coverage.Rows,
            1, 0, 0, [device], inventoryComplete: true,
            vendorCoverage: coverage);
        Assert.DoesNotContain("PRIVATE", text);
        Assert.DoesNotContain(primary, text);
        Assert.DoesNotContain(compatible, text);
    }

    [Theory]
    [InlineData(@"ROOT\NONSTANDARD", @"USB\VID_0955&PID_7019",
        PnpKnownVendorIdentity.Vendor.Nvidia)]
    [InlineData(@"PCI\CC_030000", @"PCI\VEN_10DE&DEV_28A0",
        PnpKnownVendorIdentity.Vendor.Nvidia)]
    [InlineData(@"SOUNDWIRE\AGGREGATOR", @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711",
        PnpKnownVendorIdentity.Vendor.Realtek)]
    public void GenericPrimaryStillPermitsSpecificCompatibleVendor(
        string generic, string compatible, PnpKnownVendorIdentity.Vendor vendor)
    {
        var device = Device([generic], [compatible],
            name: "Generic Windows device") with
        {
            Manufacturer = "Microsoft",
            DriverProvider = "Microsoft"
        };

        Assert.Equal(vendor, PnpKnownVendorIdentity.FromDriver(device));
        Assert.NotNull(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
    }

    [Fact]
    public void RecognizedPrimaryStillWinsOverForeignCompatible()
    {
        var device = Device(
            [@"USB\VID_0955&PID_7019"],
            [@"USB\VID_9999&PID_0001"],
            "USB", "Generic Windows USB device") with
        {
            Manufacturer = "Microsoft",
            DriverProvider = "Microsoft"
        };

        Assert.Equal(PnpKnownVendorIdentity.Vendor.Nvidia,
            PnpKnownVendorIdentity.FromDriver(device));
        Assert.Equal("NVIDIA",
            VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Equal(1, NvidiaHardwareDetector.CountNvidiaDevices([device]));
    }

    [Fact]
    public void UnknownPrimaryWithoutRecognizedCompatibleIsStillUnknown()
    {
        var device = Device([@"USB\VID_9999&PID_0001"], [],
            "USB", "Generic Windows USB device") with
        {
            Manufacturer = "Microsoft",
            DriverProvider = "Microsoft"
        };
        Assert.Null(PnpKnownVendorIdentity.FromDriver(device));
        Assert.Empty(VendorSourceDiscovery.Find([device], []));
    }

    [Fact]
    public void MalformedPrimaryIsNotPromotedToAuthoritativeVendor()
    {
        var device = Device(
            [@"USB\VID_9999&PID_0001&PRIVATE_123"],
            [@"USB\VID_0955&PID_7019"], "USB") with
        {
            Manufacturer = "Microsoft",
            DriverProvider = "Microsoft"
        };
        Assert.Null(PnpKnownVendorIdentity.FromId(device.HardwareIds[0]));
        // A malformed model carries no trusted vendor code. The
        // secondary ID remains only a read-only fallback, not a package.
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Nvidia,
            PnpKnownVendorIdentity.FromDriver(device));
    }
}
