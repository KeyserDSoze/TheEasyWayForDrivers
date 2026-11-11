using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// PnP VEN/VID is read-only OEM-source evidence, never an update offer.
/// Installed driver provider and marketing name can remain stale after a
/// DCH migration or when Windows has only its generic class driver.
/// </summary>
public sealed class PnpKnownVendorIdentityTests
{
    private static DriverInfo Device(
        string id, string displayName, string manufacturer,
        string? deviceClass = "Unknown",
        IReadOnlyList<string>? compatibles = null) =>
        new("PRIVATE_INSTANCE_SERIAL", displayName, manufacturer,
            deviceClass, manufacturer, "1.0.0.0", null, "oem42.inf",
            true, true, 0, [id], compatibles ?? []);

    [Theory]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295",
        "NVIDIA High Definition Audio", "NVIDIA", "MEDIA",
        PnpKnownVendorIdentity.Vendor.Realtek, "Realtek",
        VendorSourceDiscovery.RealtekSupportUrl)]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295&SUBSYS_12345678",
        "Intel Smart Sound Audio", "Intel", "MEDIA",
        PnpKnownVendorIdentity.Vendor.Realtek, "Realtek",
        VendorSourceDiscovery.RealtekSupportUrl)]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B&REV_01",
        "Realtek Audio", "Realtek", "MEDIA",
        PnpKnownVendorIdentity.Vendor.Nvidia, "NVIDIA",
        VendorSourceDiscovery.NvidiaDriversUrl)]
    [InlineData(@"SWC\VEN_10EC&DEV_0295&REV_01",
        "NVIDIA Software Component", "NVIDIA", "System",
        PnpKnownVendorIdentity.Vendor.Realtek, "Realtek",
        VendorSourceDiscovery.RealtekSupportUrl)]
    [InlineData(@"SWC\VEN_8086&DEV_51F1&REV_01",
        "NVIDIA Software Component", "NVIDIA", "Unknown",
        PnpKnownVendorIdentity.Vendor.Intel, "Intel",
        VendorSourceDiscovery.IntelDsaUrl)]
    [InlineData(@"SWC\VEN_10DE&DEV_28A0",
        "Realtek Software Component", "Realtek", "System",
        PnpKnownVendorIdentity.Vendor.Nvidia, "NVIDIA",
        VendorSourceDiscovery.NvidiaDriversUrl)]
    [InlineData(@"ACPI\NVDA0820&REV_0001",
        "Realtek Platform Component", "Realtek", "System",
        PnpKnownVendorIdentity.Vendor.Nvidia, "NVIDIA",
        VendorSourceDiscovery.NvidiaDriversUrl)]
    [InlineData(@"USB\VID_0CF3&PID_E300",
        "Intel Bluetooth Radio", "Intel", "Bluetooth",
        PnpKnownVendorIdentity.Vendor.Qualcomm, "Qualcomm / Atheros",
        VendorSourceDiscovery.QualcommSupportUrl)]
    [InlineData(@"USB\VID_0A5C&PID_216F",
        "Intel Bluetooth Radio", "Intel", "Bluetooth",
        PnpKnownVendorIdentity.Vendor.Broadcom, "Broadcom",
        VendorSourceDiscovery.BroadcomSupportUrl)]
    [InlineData(@"USB\VID_0E8D&PID_0003",
        "Intel Bluetooth Radio", "Intel", "Bluetooth",
        PnpKnownVendorIdentity.Vendor.MediaTek, "MediaTek",
        VendorSourceDiscovery.MediaTekSupportUrl)]
    [InlineData(@"USB\VID_0BDA&PID_1234",
        "Intel Wireless Adapter", "Intel", "Net",
        PnpKnownVendorIdentity.Vendor.Realtek, "Realtek",
        VendorSourceDiscovery.RealtekSupportUrl)]
    [InlineData(@"PCI\VEN_168C&DEV_003E",
        "Intel Network Adapter", "Intel", "Net",
        PnpKnownVendorIdentity.Vendor.Qualcomm, "Qualcomm / Atheros",
        VendorSourceDiscovery.QualcommSupportUrl)]
    public void PrimaryModelOutranksWrongMarketingBrandAndProvider(
        string hardware, string name, string manufacturer, string pnpClass,
        PnpKnownVendorIdentity.Vendor expectedIdentity,
        string expectedSource, string url)
    {
        var device = Device(hardware, name, manufacturer, pnpClass);
        Assert.Equal(expectedIdentity,
            PnpKnownVendorIdentity.FromDriver(device));

        var row = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal(expectedSource, row.Vendor);
        Assert.Equal(url, row.OfficialUrl);
        Assert.Equal(0, row.WuaMatches);
        Assert.False(row.IsVendorApplicable);
        Assert.False(row.IsCatalogMetadata);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(url));
        Assert.DoesNotContain("PRIVATE_INSTANCE_SERIAL", row.Detail);
        Assert.Contains("nessun aggiornamento OEM confermato", row.StatusText);

        Assert.Equal(expectedSource,
            VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Equal(expectedSource, DriverSourceAdvisor.GetSource(device, []));
    }

    [Fact]
    public void CompatibleIdCannotOverrideRecognizedPrimaryVendor()
    {
        var device = Device(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295",
            "NVIDIA High Definition Audio", "NVIDIA", "MEDIA",
            [@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B"]);
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Realtek,
            PnpKnownVendorIdentity.FromDriver(device));
        Assert.Equal("Realtek",
            VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Equal("Realtek", Assert.Single(
            VendorSourceDiscovery.Find([device], [])).Vendor);
    }

    [Fact]
    public void RootPrimaryIdAllowsValidCompatibleIdForSupportOnly()
    {
        var device = Device(@"ROOT\GENERIC", "Intel Bluetooth Device",
            "Intel", "Bluetooth", [@"USB\VID_0CF3&PID_E300"]);
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Qualcomm,
            PnpKnownVendorIdentity.FromDriver(device));
        Assert.Equal("Qualcomm / Atheros",
            VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.False(Assert.Single(VendorSourceDiscovery.Find([device], []))
            .IsVendorApplicable);
    }

    [Fact]
    public void ConflictingPrimaryHardwareVendorModelsDoNotChooseArbitraryOem()
    {
        var device = Device(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295",
            "NVIDIA Realtek audio", "NVIDIA", "MEDIA") with
        {
            HardwareIds = [
                @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295",
                @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B"
            ],
            CompatibleIds = [@"PCI\VEN_10DE&DEV_28A0"]
        };
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Conflicting,
            PnpKnownVendorIdentity.FromDriver(device));
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Empty(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("Windows / OEM", DriverSourceAdvisor.GetSource(device, []));
    }

    [Fact]
    public void ExplicitIneligibleCameraClassDoesNotBecomeNvidiaByStaleName()
    {
        var camera = Device(@"SWC\VEN_10EC&DEV_0295",
            "NVIDIA Camera", "NVIDIA", "Camera");
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Realtek,
            PnpKnownVendorIdentity.FromDriver(camera));
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(camera));
        Assert.Empty(VendorSourceDiscovery.Find([camera], []));
        Assert.Equal("Windows / OEM", DriverSourceAdvisor.GetSource(camera, []));
    }

    [Theory]
    [InlineData(@"USB\VID_0CF30&PID_E300")]
    [InlineData(@"USB\VID_0CF3&PID_E300\PRIVATE_INSTANCE")]
    [InlineData(@"USB\VID_0CF3&PID_E300&UID_01")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC0&DEV_0295")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295&FAKE_0000")]
    [InlineData(@"SWC\VEN_8086&DEV_51F1\PRIVATE")]
    [InlineData(@"SWC\VEN_8086&DEV_51F1&REV_0Z")]
    [InlineData(@"PCI\VEN_10DE")]
    [InlineData(@"PCI\CC_0300")]
    public void MalformedAndGenericIdsDoNotBecomeAuthoritativeVendor(
        string rawId)
    {
        Assert.Null(PnpKnownVendorIdentity.FromId(rawId));
        Assert.Null(PnpKnownVendorIdentity.FromDriver(Device(
            rawId, "Generic device", "Microsoft")));
    }

    [Theory]
    [InlineData(@"USB\VID_9999&PID_0001")]
    [InlineData(@"PCI\VEN_9999&DEV_0001")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_9999&DEV_0001")]
    public void UnknownButWellFormedVendorDoesNotBecomeKnownIdentity(
        string id)
    {
        Assert.Null(PnpKnownVendorIdentity.FromId(id));
    }

    [Theory]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&SUBSYS_09831028")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711")]
    public void ValidatedRealtekSoundWireModelOffersOnlyManualOemHandoff(
        string full)
    {
        var device = Device(full, "Generic audio codec", "Microsoft", "MEDIA");
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Realtek,
            PnpKnownVendorIdentity.FromId(full));
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Realtek,
            PnpKnownVendorIdentity.FromDriver(device));

        var handoff = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("Realtek", handoff.Vendor);
        Assert.Equal(VendorSourceDiscovery.RealtekSupportUrl, handoff.OfficialUrl);
        Assert.Equal("Realtek",
            VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Equal("Realtek", DriverSourceAdvisor.GetSource(device, []));
        Assert.Equal(0, handoff.WuaMatches);
        Assert.False(handoff.IsVendorApplicable);
        Assert.False(handoff.IsCatalogMetadata);
        Assert.DoesNotContain("PRIVATE_INSTANCE_SERIAL", handoff.Detail);
        Assert.DoesNotContain("SUBSYS", handoff.Detail);
        Assert.DoesNotContain("SUBSYS", handoff.OfficialUrl);
        Assert.Contains("nessun aggiornamento OEM confermato", handoff.StatusText);
    }

    [Theory]
    [InlineData(@"SNDW\CLASS_00&MAN_025D")]
    [InlineData(@"SNDW\CLASS_00")]
    [InlineData(@"SNDW\CLASS_00&MAN_0242&PART_0711")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&SUBSYS_0983102")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711\PRIVATE")]
    [InlineData(@"SOUNDWIRE\AGGREGATOR")]
    [InlineData(@"SOUNDWIRE\AGGREGATEDSPEAKER")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_0242&FUNC_0711")]
    public void GenericForeignOrMalformedSoundWireCannotInventRealtekSource(
        string raw)
    {
        Assert.Null(PnpKnownVendorIdentity.FromId(raw));
        var device = Device(raw, "Generic audio codec", "Microsoft", "MEDIA");
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Empty(VendorSourceDiscovery.Find([device], []));
    }

    [Fact]
    public void ConflictingPrimarySoundWireAndPciVendorFailsClosed()
    {
        var device = Device(
            @"SNDW\CLASS_00&MAN_025D&PART_0711",
            "Generic audio codec", "Microsoft", "MEDIA") with
        {
            HardwareIds =
            [
                @"SNDW\CLASS_00&MAN_025D&PART_0711",
                @"PCI\VEN_10DE&DEV_28A0"
            ],
            CompatibleIds = [@"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711"]
        };
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Conflicting,
            PnpKnownVendorIdentity.FromDriver(device));
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Empty(VendorSourceDiscovery.Find([device], []));

        var hardwareWins = device with
        {
            HardwareIds = [@"SNDW\CLASS_00&MAN_025D&PART_0711"],
            CompatibleIds = [@"PCI\VEN_10DE&DEV_28A0"]
        };
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Realtek,
            PnpKnownVendorIdentity.FromDriver(hardwareWins));
        // Vendor identity honors a validated primary hardware model, but
        // the OEM handoff conservatively vetoes a contradictory foreign
        // PCI compatible ID. Do not relax that protective policy here.
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(hardwareWins));

        var uncontested = hardwareWins with
        {
            CompatibleIds = [@"SOUNDWIRE\AGGREGATOR"]
        };
        Assert.Equal("Realtek",
            VendorSourceDiscovery.GetRecognizedSupportVendor(uncontested));
    }

    [Fact]
    public void WuaGuidCorrelationIsStillByExactPnpId()
    {
        const string id = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
        var device = Device(id, "NVIDIA High Definition Audio",
            "NVIDIA", "MEDIA");
        var update = new DriverUpdateInfo(
            "00000000-0000-4000-8000-000000000313",
            "Windows audio driver", null, "MEDIA", "Microsoft",
            null, null, false, "Microsoft", null, id, null);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(device, update));
        var handoff = Assert.Single(
            VendorSourceDiscovery.Find([device], [update]));
        Assert.Equal("Realtek", handoff.Vendor);
        Assert.Equal(1, handoff.WuaMatches);
        Assert.False(handoff.IsVendorApplicable);
    }
}
