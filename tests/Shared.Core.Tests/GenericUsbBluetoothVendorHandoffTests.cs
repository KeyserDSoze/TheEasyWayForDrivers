using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #559: strict vendor USB VID+PID + generic Bluetooth USB class can
/// restore a MANUAL support referral for an unconfigured radio, but
/// neither a Bluetooth class token nor a vendor-only hint is an update.
/// </summary>
public sealed class GenericUsbBluetoothVendorHandoffTests
{
    private const string Bluetooth = @"USB\Class_E0&SubClass_01&Prot_01";

    private static DriverInfo Radio(
        string id, string cls = "Unknown",
        IReadOnlyList<string>? compatible = null) =>
        new("PRIVATE_INSTANCE", "Generic USB Device", "Microsoft",
            cls, "Microsoft", "1.0", null, "oem999.inf",
            true, true, 0, [id], compatible ?? [Bluetooth]);

    [Theory]
    [InlineData(@"USB\VID_8087&PID_0033", "Intel",
        VendorSourceDiscovery.IntelBluetoothUrl)]
    [InlineData(@"USB\VID_0BDA&PID_8771", "Realtek",
        VendorSourceDiscovery.RealtekSupportUrl)]
    [InlineData(@"USB\VID_0CF3&PID_E300", "Qualcomm / Atheros",
        VendorSourceDiscovery.QualcommSupportUrl)]
    [InlineData(@"USB\VID_0A5C&PID_21E8", "Broadcom",
        VendorSourceDiscovery.BroadcomSupportUrl)]
    [InlineData(@"USB\VID_0E8D&PID_0003", "MediaTek",
        VendorSourceDiscovery.MediaTekSupportUrl)]
    public void UnconfiguredRadioWithExactUsbClassGetsManualSource(
        string id, string expectedVendor, string expectedUrl)
    {
        var radio = Radio(id);
        var row = Assert.Single(VendorSourceDiscovery.Find([radio], []));
        Assert.Equal(expectedVendor, row.Vendor);
        Assert.Equal(expectedUrl, row.OfficialUrl);
        Assert.Equal(expectedVendor,
            VendorSourceDiscovery.GetRecognizedSupportVendor(radio));
        Assert.Equal(0, row.WuaMatches);
        Assert.False(row.IsCatalogMetadata);
        Assert.False(row.IsVendorApplicable);
        Assert.False(row.IsInstallable);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(row.OfficialUrl));
        var safe = row.Title + row.Detail + row.StatusText;
        Assert.DoesNotContain("PRIVATE_INSTANCE", safe);
        Assert.DoesNotContain(id, safe);
        Assert.Contains("nessun aggiornamento OEM confermato", row.StatusText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Other")]
    [InlineData("USB")]
    [InlineData("System")]
    [InlineData("Unknown")]
    public void GenericDeviceClassesAreAllowedWhenRadioIdentityIsExact(
        string? cls)
    {
        var radio = Radio(@"USB\VID_0BDA&PID_8771") with
        { DeviceClass = cls };
        Assert.Equal("Realtek", Assert.Single(
            VendorSourceDiscovery.Find([radio], [])).Vendor);
    }

    [Theory]
    [InlineData(@"USB\Class_E0&SubClass_01&Prot_02")]
    [InlineData(@"USB\Class_E0&SubClass_02&Prot_01")]
    [InlineData(@"USB\Class_E0&SubClass_01")]
    [InlineData(@"USB\Class_E0&SubClass_01&Prot_01&FAKE_1")]
    [InlineData(@"USB\Class_E0&SubClass_01&Prot_01\PRIVATE_INSTANCE")]
    [InlineData("USB\\Class_E0&SubClass_01&Prot_01 ")]
    [InlineData(" USB\\Class_E0&SubClass_01&Prot_01")]
    public void ClassNearMissCannotInventUnconfiguredBluetoothVendor(
        string classId)
    {
        Assert.Empty(VendorSourceDiscovery.Find(
            [Radio(@"USB\VID_0BDA&PID_8771", compatible: [classId])], []));
    }

    [Theory]
    [InlineData("Camera")]
    [InlineData("Image")]
    [InlineData("Printer")]
    [InlineData("HIDClass")]
    [InlineData("Display")]
    [InlineData("NetService")]
    public void ExplicitForeignDeviceClassIsNotRebrandedBluetooth(string cls)
    {
        Assert.Empty(VendorSourceDiscovery.Find(
            [Radio(@"USB\VID_0CF3&PID_E300", cls)], []));
    }

    [Theory]
    [InlineData(@"USB\VID_0BDA")]
    [InlineData(@"USB\VID_0BDA&PID_8771\PRIVATE_INSTANCE")]
    [InlineData(@"USB\VID_0BDA&PID_877")]
    [InlineData(@"USB\VID_0000&PID_0001")]
    [InlineData(@"PCI\VEN_10EC&DEV_8125")]
    public void MissingUnknownOrForeignPrimaryUsbModelCannotUseClassToken(
        string primary)
    {
        Assert.Empty(VendorSourceDiscovery.Find([Radio(primary)], []));
    }

    [Fact]
    public void ForeignUsbPrimaryCannotInheritRealtekCompatibleModel()
    {
        var device = Radio(@"USB\VID_1234&PID_0001",
            compatible:
            [
                Bluetooth, @"USB\VID_0BDA&PID_8771"
            ]);
        Assert.Empty(VendorSourceDiscovery.Find([device], []));
    }

    [Fact]
    public void GenericBluetoothClassAloneIsNotWuaDeviceEvidence()
    {
        var device = Radio(@"USB\VID_0BDA&PID_8771");
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(Bluetooth));
        Assert.False(VendorSourceDiscovery.Find([device], [])
            .Any(row => row.IsInstallable));
        Assert.Equal("Realtek", Assert.Single(
            VendorSourceDiscovery.Find([device], [])).Vendor);
    }
}
