using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #410: a malformed primary PCI VEN+DEV suffix is not a trusted vendor.
/// It must not veto an independently validated NVIDIA/Killer compatible
/// model. This is manual OEM support, never a driver update offer.
/// </summary>
public sealed class PciForeignVendorMalformedSuffixTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Intel = @"PCI\VEN_8086&DEV_46A6";

    private static DriverInfo Device(
        string primary, string compatible, string name = "Microsoft Basic Display Adapter",
        string deviceClass = "Display") =>
        new("PRIVATE_INSTANCE_SERIAL_410", name, "Microsoft",
            deviceClass, "Microsoft", "31.0.15.4200", null, "oem42.inf",
            true, true, 0, [primary], [compatible]);

    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_46A6&FAKE_01")]
    [InlineData(@"PCI\VEN_8086&DEV_46A6&SUBSYS_NOTHEX")]
    [InlineData(@"PCI\VEN_8086&DEV_46A6&SUBSYS_1234")]
    [InlineData(@"PCI\VEN_8086&DEV_46A6&REV_GG")]
    [InlineData(@"PCI\VEN_8086&DEV_46A6&CC_XXXX")]
    [InlineData(@"PCI\VEN_8086&DEV_46A6&REV_01&FAKE_00")]
    [InlineData(@"PCI\VEN_8086&DEV_46A6&EXTRA_00112233")]
    public void InvalidForeignPrimaryCannotHideValidNvidiaCompatibleModel(
        string malformedPrimary)
    {
        var device = Device(malformedPrimary, Nvidia);
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Nvidia,
            PnpKnownVendorIdentity.FromDriver(device));

        var source = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("NVIDIA", source.Vendor);
        Assert.Equal(VendorSourceDiscovery.NvidiaDriversUrl, source.OfficialUrl);
        Assert.Equal("NVIDIA", VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Equal(0, source.WuaMatches);
        Assert.False(source.IsInstallable);
        Assert.False(source.IsVendorApplicable);
        Assert.Contains("nessun aggiornamento OEM confermato", source.StatusText);
        Assert.DoesNotContain("PRIVATE_INSTANCE", source.Detail);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&FAKE_01")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_BROKEN")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&REV_0Z")]
    public void InvalidNvidiaPrimaryCannotHideValidIntelCompatibleModel(
        string malformedPrimary)
    {
        var device = Device(malformedPrimary, Intel,
            "Microsoft Basic Display Adapter");
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Intel,
            PnpKnownVendorIdentity.FromDriver(device));

        var source = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("Intel", source.Vendor);
        Assert.Equal(VendorSourceDiscovery.IntelDsaUrl, source.OfficialUrl);
        Assert.Equal("Intel", VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.False(source.IsInstallable);
    }

    [Fact]
    public void MalformedForeignPrimaryStillAllowsStrictKillerCompatibleModel()
    {
        const string killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
        var device = Device(@"PCI\VEN_10DE&DEV_28A0&EXTRA_00",
            killer, name: "Killer Wi-Fi AX1675i", deviceClass: "Net");
        var source = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("Killer / Intel", source.Vendor);
        Assert.Equal(VendorSourceDiscovery.IntelWifiUrl, source.OfficialUrl);
        Assert.False(source.IsInstallable);
    }

    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_46A6")]
    [InlineData(@"PCI\VEN_8086&DEV_46A6&SUBSYS_16A217AA")]
    [InlineData(@"PCI\VEN_8086&DEV_46A6&REV_01")]
    [InlineData(@"PCI\VEN_8086&DEV_46A6&CC_030000")]
    public void GenuineForeignPciModelsStillVetoNvidiaNameAndCompatibleId(
        string validIntelPrimary)
    {
        var device = Device(validIntelPrimary, Nvidia,
            name: "NVIDIA GeForce RTX 4060 Laptop GPU");
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Intel,
            PnpKnownVendorIdentity.FromDriver(device));
        var source = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("Intel", source.Vendor);
        Assert.Equal(VendorSourceDiscovery.IntelDsaUrl, source.OfficialUrl);
        Assert.Equal("Intel", VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.False(source.IsInstallable);
    }

    [Fact]
    public void TwoGenuineForeignPrimaryVendorsStillFailClosed()
    {
        var device = Device(Nvidia, Intel, name: "Intel Graphics")
            with { HardwareIds = [Nvidia, Intel] };

        Assert.Equal(PnpKnownVendorIdentity.Vendor.Conflicting,
            PnpKnownVendorIdentity.FromDriver(device));
        Assert.Empty(VendorSourceDiscovery.Find([device], []));
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
    }

    [Theory]
    [InlineData(@"PCI\VEN_8086")]
    [InlineData(@"PCI\VEN_8086&DEV_")]
    [InlineData(@"PCI\VEN_8086&DEV_46A6\PRIVATE_SERIAL")]
    [InlineData(@"PCI\VEN_8086&DEV_46A6" + "\r\n")]
    [InlineData(@"PCI\VEN_8086&DEV_46A6" + "\u202E")]
    public void IncompleteOrPrivatePciIdNeverActsAsForeignVendor(string id)
    {
        var device = Device(id, Nvidia);
        var source = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("NVIDIA", source.Vendor);
        Assert.Equal(VendorSourceDiscovery.NvidiaDriversUrl, source.OfficialUrl);
        Assert.False(source.IsInstallable);
    }
}
