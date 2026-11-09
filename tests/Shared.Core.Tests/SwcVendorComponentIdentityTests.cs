using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class SwcVendorComponentIdentityTests
{
    private static DriverInfo Device(string instance, string id,
        string deviceClass = "SoftwareComponent",
        IReadOnlyList<string>? compatible = null,
        string manufacturer = "Microsoft") =>
        new(instance, "Software Component", manufacturer, deviceClass,
            "Microsoft", "31.0.0.1", null, "oem42.inf",
            true, true, 0, [id], compatible ?? []);

    [Theory]
    [InlineData(@"SWC\VEN_8086&DEV_51F1", "Intel")]
    [InlineData(@"swc\ven_8086&dev_51f1&REV_01", "Intel")]
    [InlineData(@"SWC\VEN_10EC&DEV_0295", "Realtek")]
    [InlineData(@"swc\ven_10ec&dev_0295&SUBSYS_12345678", "Realtek")]
    public void GenericMicrosoftDchComponentsHaveOfficialReadOnlyHandoff(
        string id, string expectedVendor)
    {
        var component = Device("PRIVATE_DCH", id);
        var guid = Guid.NewGuid().ToString("D");
        var offer = new DriverUpdateInfo(guid, "Synthetic WUA driver",
            null, "SoftwareComponent", "Microsoft", null, null, false,
            "Microsoft", null, id, null);
        var handoff = Assert.Single(VendorSourceDiscovery.Find(
            [component], [offer]));
        var installed = Assert.Single(VendorDriverEvidenceFinder.Find(
            [component], [offer]));

        Assert.Equal(expectedVendor, handoff.Vendor);
        Assert.Equal(expectedVendor, installed.Vendor);
        Assert.Equal(1, handoff.WuaMatches);
        Assert.Equal(1, installed.CorrelatedWuaCandidates);
        Assert.False(handoff.IsVendorApplicable);
        Assert.False(handoff.IsCatalogMetadata);
        Assert.Equal(expectedVendor == "Intel"
                ? VendorSourceDiscovery.IntelDsaUrl
                : VendorSourceDiscovery.RealtekSupportUrl,
            handoff.OfficialUrl);
        Assert.Contains("stato OEM non verificato",
            installed.DisplayLine);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(
            handoff.OfficialUrl));
        Assert.DoesNotContain("PRIVATE_DCH", installed.DisplayLine);
        Assert.DoesNotContain(id, installed.DisplayLine);
    }

    [Theory]
    [InlineData(@"SWC\VEN_8086&DEV_51F1", "System")]
    [InlineData(@"SWC\VEN_10EC&DEV_0295", "Camera")]
    [InlineData(@"SWC\VEN_80860&DEV_51F1", "SoftwareComponent")]
    [InlineData(@"SWC\VEN_10EC0&DEV_0295", "SoftwareComponent")]
    [InlineData(@"SWC\VEN_8086&DEV_51F1F", "SoftwareComponent")]
    [InlineData(@"SWC\VEN_10EC&DEV_0295BAD", "Extension")]
    [InlineData(@"SWC\VEN_8086&DEV_51F", "Extension")]
    [InlineData(@"SWC\VEN_10EC&DEV_G295", "SoftwareComponent")]
    [InlineData(@"ROOT\SOFTWARECOMPONENT", "SoftwareComponent")]
    public void WrongClassOrNearMissVendorAndModelNeverCreatesHandoff(
        string id, string deviceClass)
    {
        var component = Device("PRIVATE_INVALID", id, deviceClass);
        Assert.False(SwcVendorComponentIdentity.IsIntel(component));
        Assert.False(SwcVendorComponentIdentity.IsRealtek(component));
        Assert.Empty(VendorSourceDiscovery.Find([component], []));
        Assert.Empty(VendorDriverEvidenceFinder.Find([component], []));
    }

    [Fact]
    public void CompatibleIdCanRecoverRealtekDchWithMicrosoftGenericName()
    {
        const string realtek = @"SWC\VEN_10EC&DEV_0295";
        var component = Device("PRIVATE_COMPAT", @"ROOT\SOFTWARE",
            compatible: [realtek]);
        Assert.True(SwcVendorComponentIdentity.IsRealtek(component));
        Assert.Equal("Realtek",
            VendorSourceDiscovery.GetRecognizedSupportVendor(component));
        Assert.Equal("Realtek",
            Assert.Single(VendorDriverEvidenceFinder.Find(
                [component], [])).Vendor);
    }

    [Fact]
    public void GenericIntelSoftwareComponentWithoutWifiNameIsNotAProsetOffer()
    {
        const string intel = @"SWC\VEN_8086&DEV_51F1";
        var component = Device("PRIVATE_GENERIC_INTEL", intel);
        Assert.True(SwcVendorComponentIdentity.IsIntel(component));
        var handoff = Assert.Single(VendorSourceDiscovery.Find(
            [component], []));
        Assert.Equal("Intel", handoff.Vendor);
        Assert.Equal(0, handoff.WuaMatches);
        Assert.Contains("nessun aggiornamento OEM confermato",
            handoff.StatusText);
        Assert.DoesNotContain("PRIVATE_GENERIC_INTEL", handoff.StatusText);
    }

    [Fact]
    public void RealtekDeviceIdWinsOverIntelDriverProvider()
    {
        var component = Device("PRIVATE_REALTEK",
            @"SWC\VEN_10EC&DEV_0295",
            manufacturer: "Intel Smart Sound Technology");
        var handoff = Assert.Single(VendorSourceDiscovery.Find(
            [component], []));
        Assert.Equal("Realtek", handoff.Vendor);
        Assert.Equal("Realtek",
            Assert.Single(VendorDriverEvidenceFinder.Find(
                [component], [])).Vendor);
    }
}
