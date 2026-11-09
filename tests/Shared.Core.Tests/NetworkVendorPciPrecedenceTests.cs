using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Primary PCI VEN+DEV must outrank stale WMI marketing names in read-only
/// network-vendor handoffs. These rows are not OEM update offers.
/// </summary>
public sealed class NetworkVendorPciPrecedenceTests
{
    private static DriverInfo Device(string id, string name, string provider) =>
        new("PRIVATE_INSTANCE", name, provider, "Net", provider,
            "1.0.0.0", null, "oem42.inf", true, true, 0, [id], []);

    [Theory]
    [InlineData(@"PCI\VEN_14E4&DEV_43A0", "Realtek Network Adapter",
        "Realtek", "Broadcom", VendorSourceDiscovery.BroadcomSupportUrl)]
    [InlineData(@"PCI\VEN_168C&DEV_003E", "Broadcom Wireless Adapter",
        "Broadcom", "Qualcomm / Atheros", VendorSourceDiscovery.QualcommSupportUrl)]
    [InlineData(@"PCI\VEN_17CB&DEV_1101", "Realtek Network Adapter",
        "Realtek", "Qualcomm / Atheros", VendorSourceDiscovery.QualcommSupportUrl)]
    [InlineData(@"PCI\VEN_14C3&DEV_0608", "Qualcomm Wireless Adapter",
        "Qualcomm", "MediaTek", VendorSourceDiscovery.MediaTekSupportUrl)]
    [InlineData(@"PCI\VEN_10EC&DEV_8125", "Broadcom Network Adapter",
        "Broadcom", "Realtek", VendorSourceDiscovery.RealtekSupportUrl)]
    public void StrictPciIdentityWinsOverConflictingProvider(
        string id, string title, string provider, string vendor, string officialUrl)
    {
        var device = Device(id, title, provider);

        var row = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal(vendor, row.Vendor);
        Assert.Equal(officialUrl, row.OfficialUrl);
        Assert.Equal(vendor, VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Equal(vendor, DriverSourceAdvisor.GetSource(device, []));
        Assert.Equal(vendor, Assert.Single(
            VendorDriverEvidenceFinder.Find([device], [])).Vendor);
        Assert.Equal(0, row.WuaMatches);
        Assert.False(row.IsVendorApplicable);
        Assert.DoesNotContain("PRIVATE_INSTANCE", row.Detail);
        Assert.DoesNotContain("oem42.inf", row.Detail);
    }

    [Fact]
    public void CompatiblePciVendorUsedOnlyWhenHardwareHasNoValidPciId()
    {
        var fallback = Device(@"ROOT\GENERIC", "Realtek Network Adapter",
            "Realtek") with
        {
            CompatibleIds = [@"PCI\VEN_14E4&DEV_43A0"]
        };
        Assert.Equal("Broadcom",
            VendorSourceDiscovery.GetRecognizedSupportVendor(fallback));

        var primary = Device(@"PCI\VEN_14E4&DEV_43A0",
            "Realtek Network Adapter", "Realtek") with
        {
            CompatibleIds = [@"PCI\VEN_10EC&DEV_8125"]
        };
        Assert.Equal("Broadcom",
            VendorSourceDiscovery.GetRecognizedSupportVendor(primary));
    }

    [Theory]
    [InlineData(@"PCI\VEN_14E4&DEV_")]
    [InlineData(@"PCI\VEN_14E4&DEV_43A")]
    [InlineData(@"PCI\VEN_14E4&DEV_43A00")]
    [InlineData(@"PCI\VEN_14E4&DEV_43A0\PRIVATE")]
    [InlineData(@"PCI\VEN_14E40&DEV_43A0")]
    public void MalformedPciTokensCannotCreateVendorHandoff(string id)
    {
        var device = Device(id, "Network Controller", "Microsoft");
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Empty(VendorSourceDiscovery.Find([device], []));
    }

    [Fact]
    public void UnknownValidPciVendorCannotBeRebrandedByStaleName()
    {
        var device = Device(@"PCI\VEN_9999&DEV_0001",
            "Broadcom Wireless Adapter", "Broadcom");
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Empty(VendorSourceDiscovery.Find([device], []));
    }

    [Fact]
    public void NonPciUsbAndHdaudioSourcesRemainRecognized()
    {
        var usb = Device(@"USB\VID_0CF3&PID_E300",
            "Generic radio", "Microsoft") with { DeviceClass = "Bluetooth" };
        var audio = Device(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295",
            "Generic audio", "Microsoft") with { DeviceClass = "MEDIA" };

        Assert.Equal("Qualcomm / Atheros",
            VendorSourceDiscovery.GetRecognizedSupportVendor(usb));
        Assert.Equal("Realtek",
            VendorSourceDiscovery.GetRecognizedSupportVendor(audio));
    }
}
