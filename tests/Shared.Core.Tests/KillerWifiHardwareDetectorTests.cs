using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class KillerWifiHardwareDetectorTests
{
    private static DriverInfo Device(string hardware) =>
        new("PRIVATE_INSTANCE", "Network Controller", "Microsoft",
            "Net", "Microsoft", "1.0", null, "oem4.inf", true,
            true, 0, [hardware], []);

    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56")]
    [InlineData(@"PCI\VEN_8086&DEV_51F0&SUBSYS_16721A56&REV_01")]
    [InlineData(@"pci\ven_8086&dev_51f1&subsys_16721a56&rev_01")]
    public void ExactKillerSubsystemSurvivesGenericWindowsTitle(string id) =>
        Assert.True(KillerWifiHardwareDetector.IsKnownAx1675i(Device(id)));

    [Fact]
    public void FullCompatibleIdWorksButBareIntelCompatibleIdDoesNot()
    {
        var card = Device(@"PCI\VEN_9999&DEV_0001") with
        {
            CompatibleIds = [@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56"]
        };
        Assert.True(KillerWifiHardwareDetector.IsKnownAx1675i(card));
        Assert.False(KillerWifiHardwareDetector.IsKnownAx1675i(card with
        {
            CompatibleIds = [@"PCI\VEN_8086&DEV_51F1"]
        }));
    }

    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_51F1")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_00008086")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A560")]
    [InlineData(@"PCI\VEN_8086&DEV_51F10&SUBSYS_16721A56")]
    [InlineData(@"PCI\VEN_80860&DEV_51F1&SUBSYS_16721A56")]
    [InlineData(@"PCI\VEN_8086&DEV_51F2&SUBSYS_16721A56")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56X")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56\PRIVATE")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56" + "\n")]
    public void NearMissAndOtherIntelHardwareNeverProveKillerBrand(string id) =>
        Assert.False(KillerWifiHardwareDetector.IsKnownAx1675i(Device(id)));

    [Fact]
    public void OnlyNetworkNodesMayBeRecognized()
    {
        var card = Device(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56");
        Assert.False(KillerWifiHardwareDetector.IsKnownAx1675i(
            card with { DeviceClass = "Bluetooth" }));
        Assert.False(KillerWifiHardwareDetector.IsKnownAx1675i(
            card with { DeviceClass = "Camera" }));
        Assert.True(KillerWifiHardwareDetector.IsKnownAx1675i(
            card with { DeviceClass = null }));
        Assert.False(KillerWifiHardwareDetector.IsKnownAx1675i(
            card with { DeviceClass = null, Name = "Camera" }));
    }

    [Fact]
    public void GenericUnknownKillerCanUseExactPciNetworkClassCode()
    {
        const string id = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
        var generic = Device(id) with
        {
            Name = "PCI Device", DeviceClass = "Unknown",
            CompatibleIds = [@"PCI\CC_028000"]
        };
        Assert.True(KillerWifiHardwareDetector.IsKnownAx1675i(generic));
        Assert.False(KillerWifiHardwareDetector.IsKnownAx1675i(
            generic with { CompatibleIds = [@"PCI\CC_030000"] }));
        Assert.False(KillerWifiHardwareDetector.IsKnownAx1675i(
            generic with { DeviceClass = "Camera" }));
        Assert.False(KillerWifiHardwareDetector.IsKnownAx1675i(
            generic with { HardwareIds = [@"PCI\VEN_8086&DEV_51F1"] }));
    }


}
