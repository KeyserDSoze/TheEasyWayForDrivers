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
    public void FakeKillerCompatibleIdDoesNotUpgradeGenericIntelRadio()
    {
        var generic = Device(@"PCI\VEN_8086&DEV_51F1") with
        {
            Name = "PCI Device",
            DeviceClass = "Unknown",
            CompatibleIds =
            [
                @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56&FAKE_01",
                @"PCI\CC_028000"
            ]
        };

        Assert.False(KillerWifiHardwareDetector.IsKnownAx1675i(generic));
        Assert.Equal(0,
            ReferenceDriverCoverage.Create([generic], null)[4].Devices);
        Assert.DoesNotContain(VendorSourceDiscovery.Find([generic], []),
            row => row.Vendor == "Killer / Intel");
    }

    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56&FAKE_01")]
    [InlineData(@"PCI\VEN_8086&DEV_51F0&SUBSYS_16721A56&CC_0280")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56&REV_1")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56&REV_001")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56&REV_0G")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56&REV_01&FAKE")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56&REV_01&REV_02")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56&REV_")]
    public void ArbitraryPciSuffixNeverProvesKillerAx1675i(string id)
    {
        var device = Device(id);
        Assert.False(KillerWifiHardwareDetector.IsKnownAx1675i(device));

        // The eight-row QA benchmark and official manual support source
        // must not rebrand a generic Intel card as Killer.
        var row = ReferenceDriverCoverage.Create([device], null)[4];
        Assert.Equal(0, row.Devices);
        Assert.DoesNotContain(VendorSourceDiscovery.Find([device], []),
            source => source.Vendor == "Killer / Intel");
        Assert.NotEqual("Killer / Intel",
            VendorSourceDiscovery.GetRecognizedSupportVendor(device));
    }

    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_51F0&SUBSYS_16721A56")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56&REV_00")]
    [InlineData(@"PCI\VEN_8086&DEV_51F0&SUBSYS_16721A56&REV_fF")]
    public void GenuineKillerModelRemainsVisibleInEightRowAndOfficialHandoff(
        string id)
    {
        var device = Device(id);
        Assert.True(KillerWifiHardwareDetector.IsKnownAx1675i(device));
        Assert.Equal(1,
            ReferenceDriverCoverage.Create([device], null)[4].Devices);

        var source = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("Killer / Intel", source.Vendor);
        Assert.Equal(VendorSourceDiscovery.IntelWifiUrl, source.OfficialUrl);
        Assert.Equal(0, source.WuaMatches);
        Assert.False(source.IsVendorApplicable);
    }

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
