using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #405: a missing WMI Name must not hide a strictly identified NVIDIA,
/// Intel/Killer or Realtek PnP device from manual OEM support preview.
/// Such a link is never a vendor driver offer or install permission.
/// </summary>
public sealed class VendorHandoffNamelessPnpTests
{
    private static DriverInfo Device(string? name, string id,
        string? pnpClass, string manufacturer = "Microsoft",
        string? provider = null) =>
        new(@"PRIVATE_DEVICE\SERIAL_123456", name!, manufacturer,
            pnpClass, provider ?? manufacturer, "32.0.0.1", null,
            "oem42.inf", true, true, 0, [id], []);

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0", "Display", "NVIDIA",
        VendorSourceDiscovery.NvidiaDriversUrl)]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B", "MEDIA", "NVIDIA",
        VendorSourceDiscovery.NvidiaDriversUrl)]
    [InlineData(@"ACPI\NVDA0820", "System", "NVIDIA",
        VendorSourceDiscovery.NvidiaDriversUrl)]
    [InlineData(@"SWC\VEN_10DE&DEV_28A0", "SoftwareComponent", "NVIDIA",
        VendorSourceDiscovery.NvidiaDriversUrl)]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56", "Net",
        "Killer / Intel", VendorSourceDiscovery.IntelWifiUrl)]
    [InlineData(@"PCI\VEN_8086&DEV_46A6", "Display", "Intel",
        VendorSourceDiscovery.IntelDsaUrl)]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295", "MEDIA", "Realtek",
        VendorSourceDiscovery.RealtekSupportUrl)]
    [InlineData(@"PCI\VEN_14E4&DEV_43A0", "Net", "Broadcom",
        VendorSourceDiscovery.BroadcomSupportUrl)]
    public void StrictPnpModelKeepsNamelessDeviceInOfficialManualPreview(
        string model, string deviceClass, string vendor, string url)
    {
        var driver = Device("  ", model, deviceClass);
        var row = Assert.Single(VendorSourceDiscovery.Find([driver], []));

        Assert.Equal(vendor, row.Vendor);
        Assert.Equal(url, row.OfficialUrl);
        Assert.Equal(PnpDeviceDisplayName.Create("", deviceClass),
            row.DeviceName);
        Assert.Equal("32.0.0.1", row.InstalledVersion);
        Assert.Equal(0, row.WuaMatches);
        Assert.False(row.IsInstallable);
        Assert.False(row.IsVendorApplicable);
        Assert.False(row.IsCatalogMetadata);
        Assert.Contains("nessun aggiornamento OEM confermato", row.StatusText);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(row.OfficialUrl));
        Assert.DoesNotContain("PRIVATE_DEVICE", row.DeviceName);
        Assert.DoesNotContain("SERIAL_123456", row.Detail);
        Assert.Equal(vendor, VendorSourceDiscovery.GetRecognizedSupportVendor(driver));
    }

    [Fact]
    public void NullNameWithValidNvidiaModelIsHandledWithoutAnyRawIdLeak()
    {
        var driver = Device(null, @"PCI\VEN_10DE&DEV_28A0", "Display");
        var row = Assert.Single(VendorSourceDiscovery.Find([driver], []));
        Assert.Equal("Scheda video (nome non disponibile)", row.Title);
        Assert.DoesNotContain("SERIAL", row.Detail);
        Assert.Equal("NVIDIA", DriverSourceAdvisor.GetSource(driver, []));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE")]
    [InlineData(@"PCI\VEN_10DE&DEV_")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&FAKE_01")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE")]
    [InlineData(@"USB\VID_0955")]
    [InlineData(@"ROOT\NVIDIA")]
    [InlineData(@"PCI\CC_0300")]
    public void NamelessDeviceWithOnlyNonModelIdCannotUseBrandFallback(
        string fakeModel)
    {
        var driver = Device(null, fakeModel, "Display",
            manufacturer: "NVIDIA", provider: "NVIDIA");
        Assert.Empty(VendorSourceDiscovery.Find([driver], []));
    }

    [Fact]
    public void ConflictingHardwareVendorsOnSameInstanceRemainExcluded()
    {
        var nvidia = Device(null, @"PCI\VEN_10DE&DEV_28A0",
            "Display");
        var realtek = Device(" ", @"PCI\VEN_10EC&DEV_8125",
            "Net");
        Assert.Empty(VendorSourceDiscovery.Find([nvidia, realtek], []));
    }

    [Fact]
    public void ForeignKnownPrimaryKeepsIntelHandoffAndRejectsNvidiaCompatibleModel()
    {
        var device = Device(null, @"PCI\VEN_8086&DEV_46A6", "Display",
            manufacturer: "NVIDIA", provider: "NVIDIA") with
        {
            CompatibleIds = [@"PCI\VEN_10DE&DEV_28A0"]
        };
        // Validated Intel PCI primary outranks the stale NVIDIA provider
        // and weaker NVIDIA compatible model. It must not suppress Intel.
        var result = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("Intel", result.Vendor);
        Assert.Equal(VendorSourceDiscovery.IntelDsaUrl, result.OfficialUrl);
        Assert.Equal("Scheda video (nome non disponibile)", result.Title);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void CorrelationKeepsExactWuaGuidWithoutInventingOemUpdate()
    {
        const string model = @"PCI\VEN_10DE&DEV_28A0";
        var device = Device(null, model, "Display");
        var update = new DriverUpdateInfo(
            "00000000-0000-4000-8000-000000000405",
            "Windows display driver", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, model, null);
        var hit = Assert.Single(VendorSourceDiscovery.Find([device], [update]));
        var noHit = Assert.Single(VendorSourceDiscovery.Find([device], []));

        Assert.Equal(1, hit.WuaMatches);
        Assert.Equal(0, noHit.WuaMatches);
        Assert.False(hit.IsInstallable);
        Assert.False(hit.IsVendorApplicable);
        Assert.False(noHit.IsInstallable);
        Assert.DoesNotContain("PRIVATE_DEVICE", hit.Title);
    }

    [Fact]
    public void KnownNameWithUnrecognizedHardwareKeepsLegacyBehavior()
    {
        var device = Device("Network Controller", @"PCI\VEN_14E4&DEV_43A0", "Net");
        var result = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("Network Controller", result.DeviceName);
        Assert.Equal("Broadcom", result.Vendor);
    }

    [Fact]
    public void NamelessMissingIdsAndOnlyManufacturerNeverCreateOffer()
    {
        var empty = Device(" ", @"ROOT\UNKNOWN", "System",
            manufacturer: "NVIDIA", provider: "NVIDIA");
        var invalid = Device(null, @"PCI\VEN_9999&DEV_1234", "Display",
            manufacturer: "NVIDIA", provider: "NVIDIA");
        Assert.Empty(VendorSourceDiscovery.Find([empty, invalid], []));
    }
}
