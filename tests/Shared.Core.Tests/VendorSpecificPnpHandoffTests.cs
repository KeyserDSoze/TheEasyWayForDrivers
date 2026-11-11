using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// OEM support links must be grounded in complete validated public PnP
/// models, not generic USB vendor/audio function hints or private paths.
/// Source handoffs are read-only; nothing here claims an OEM driver offer.
/// </summary>
public sealed class VendorSpecificPnpHandoffTests
{
    private static DriverInfo Device(
        string hardware, string deviceClass,
        string name = "Generic Windows device") =>
        new("PRIVATE_INSTANCE_SERIAL", name, "Microsoft",
            deviceClass, "Microsoft", "1.0.0.0", null, "oem42.inf",
            true, true, 0, [hardware], []);

    [Theory]
    [InlineData(@"USB\VID_0CF3", "Bluetooth")]
    [InlineData(@"USB\VID_0A5C&FAKE_00", "Bluetooth")]
    [InlineData(@"USB\VID_0E8D&PID_7961&SERIAL_PRIVATE", "Bluetooth")]
    [InlineData(@"USB\VID_0E8D&PID_7961\PRIVATE_INSTANCE", "Bluetooth")]
    [InlineData(@"USB\VID_0BDA", "Net")]
    [InlineData(@"USB\VID_0BDA&PID_123", "Net")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC", "MEDIA")]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC&FAKE_0001", "MEDIA")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295\PRIVATE_INSTANCE", "MEDIA")]
    [InlineData(@"PCI\VEN_14E4&DEV_43A0&FAKE_00", "Net")]
    [InlineData(@"PCI\VEN_168C&DEV_003E&FAKE_00", "Net")]
    [InlineData(@"PCI\VEN_14C3&DEV_0608&SERIAL_PRIVATE", "Net")]
    public void GenericMalformedAndPrivateModelsCannotInventAnOemSource(
        string hardware, string deviceClass)
    {
        var driver = Device(hardware, deviceClass);
        Assert.Null(PnpKnownVendorIdentity.FromId(hardware));
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(driver));
        Assert.Empty(VendorSourceDiscovery.Find([driver], []));
        Assert.Empty(VendorDriverEvidenceFinder.Find([driver], []));
        Assert.Equal("Windows / OEM",
            DriverSourceAdvisor.GetSource(driver, []));
    }

    [Theory]
    [InlineData(@"USB\VID_0CF3&PID_E300", "Bluetooth", "Qualcomm / Atheros")]
    [InlineData(@"USB\VID_0A5C&PID_21E8", "Bluetooth", "Broadcom")]
    [InlineData(@"USB\VID_0E8D&PID_7961", "Bluetooth", "MediaTek")]
    [InlineData(@"USB\VID_0BDA&PID_B812", "Net", "Realtek")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295", "MEDIA", "Realtek")]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295&SUBSYS_09831028", "MEDIA", "Realtek")]
    [InlineData(@"PCI\VEN_14E4&DEV_43A0&SUBSYS_00001028", "Net", "Broadcom")]
    [InlineData(@"PCI\VEN_168C&DEV_003E", "Net", "Qualcomm / Atheros")]
    [InlineData(@"PCI\VEN_14C3&DEV_0608", "Net", "MediaTek")]
    public void CompleteSpecificModelKeepsSupportHandoffWithMicrosoftName(
        string hardware, string deviceClass, string expectedVendor)
    {
        var driver = Device(hardware, deviceClass);
        var row = Assert.Single(VendorSourceDiscovery.Find([driver], []));
        var evidence = Assert.Single(VendorDriverEvidenceFinder.Find([driver], []));

        Assert.NotNull(MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(hardware));
        Assert.Equal(expectedVendor,
            VendorSourceDiscovery.GetRecognizedSupportVendor(driver));
        Assert.Equal(expectedVendor, row.Vendor);
        Assert.Equal(expectedVendor, evidence.Vendor);
        Assert.Equal(expectedVendor, DriverSourceAdvisor.GetSource(driver, []));
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(row.OfficialUrl));
        Assert.False(row.IsVendorApplicable);
        Assert.False(row.IsCatalogMetadata);
        Assert.Equal(0, evidence.CorrelatedWuaCandidates);
        Assert.DoesNotContain("PRIVATE_INSTANCE", evidence.DisplayLine);
        Assert.DoesNotContain("SUBSYS", evidence.DisplayLine);
        Assert.DoesNotContain("PRIVATE_INSTANCE", row.Detail);
    }

    [Fact]
    public void CompatibleIdNeedsValidatedSpecificModelEvenWithGenericPrimary()
    {
        var generic = Device(@"ROOT\GENERIC", "Bluetooth") with
        {
            CompatibleIds = [@"USB\VID_0CF3"]
        };
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(generic));
        Assert.Empty(VendorDriverEvidenceFinder.Find([generic], []));

        var recovered = generic with
        {
            CompatibleIds = [@"USB\VID_0CF3&PID_E300"]
        };
        Assert.Equal("Qualcomm / Atheros",
            VendorSourceDiscovery.GetRecognizedSupportVendor(recovered));
        Assert.Equal("Qualcomm / Atheros",
            Assert.Single(VendorDriverEvidenceFinder.Find([recovered], [])).Vendor);
    }

    [Fact]
    public void ConflictingTrustedPciPrimaryStillVetoesForeignUsbCompatible()
    {
        var driver = Device(@"PCI\VEN_10DE&DEV_28A0", "Bluetooth") with
        {
            CompatibleIds = [@"USB\VID_0CF3&PID_E300"]
        };
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Nvidia,
            PnpKnownVendorIdentity.FromDriver(driver));
        Assert.NotEqual("Qualcomm / Atheros",
            VendorSourceDiscovery.GetRecognizedSupportVendor(driver));
    }
}
