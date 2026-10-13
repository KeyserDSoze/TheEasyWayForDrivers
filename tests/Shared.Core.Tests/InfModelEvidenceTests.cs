using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class InfModelEvidenceTests
{
    private const string Inf = """
        [Version]
        Signature="$Windows NT$"
        [Manufacturer]
        %Vendor% = Models,NTamd64
        [Models.NTamd64]
        %Device%=Install, PCI\VEN_8086&DEV_1234&SUBSYS_00000001, PCI\VEN_8086&DEV_1234 ; safe
        %Usb%=UsbInstall, USB\VID_046D&PID_C534
        [NotModels]
        Fake=Install, PCI\VEN_9999&DEV_1234
        """;

    private static DriverInfo Device() =>
        new("id", "Test", "Intel", "System", "Intel", "1.0",
            null, null, true, true, 0,
            [@"PCI\VEN_8086&DEV_1234&SUBSYS_00000001"],
            [@"PCI\VEN_8086&DEV_1234"]);

    [Fact]
    public void ReadsOnlyManufacturerReferencedModelSections()
    {
        var ids = InfModelEvidence.ReadDeclaredIds(Inf);
        Assert.Equal(3, ids.Count);
        Assert.DoesNotContain(ids, x => x.HardwareId.Contains("9999"));
        Assert.Equal(2, InfModelEvidence.MatchStrength(Device(), ids));
    }

    [Fact]
    public void CompatibleIdAloneIsWeakerEvidence()
    {
        var ids = new[] { new InfModelEvidence.DeclaredId("Models", @"PCI\VEN_8086&DEV_1234") };
        Assert.Equal(1, InfModelEvidence.MatchStrength(Device(), ids));
    }

    [Theory]
    [InlineData("[Models]\nA=Install, PCI\\VEN_8086&DEV_1234")]
    [InlineData("[Manufacturer]\nVendor=Models\n[Other]\nA=Install, PCI\\VEN_8086&DEV_1234")]
    [InlineData("")]
    public void UnsupportedOrUnreferencedSectionsYieldNoEvidence(string content) =>
        Assert.Empty(InfModelEvidence.ReadDeclaredIds(content));

    [Theory]
    [InlineData(@"HID\VID_046D&PID_C534")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0255")]
    [InlineData(@"BTHENUM\DEV_001122334455")]
    [InlineData(@"BTHLEDEVICE\DEV_AABBCCDDEEFF")]
    [InlineData(@"USBSTOR\DISK&VEN_SANDISK&PROD_ULTRA")]
    [InlineData(@"SCSI\DISK&VEN_NVME&PROD_SSD")]
    [InlineData(@"SD\VID_1B&PID_2040")]
    [InlineData(@"DISPLAY\BOE0A1B")]
    [InlineData(@"MONITOR\DEL4098")]
    [InlineData(@"ROOT\MEDIA")]
    [InlineData(@"SWD\MMDEVAPI")]
    [InlineData(@"VMBUS\{00112233-4455-6677-8899-AABBCCDDEEFF}")]
    [InlineData(@"SERENUM\PNP0501")]
    [InlineData(@"*PNP0F01")]
    public void ExtendedWindowsEnumeratorsAreDiscoveredAsMetadataOnly(string hardwareId)
    {
        var inf = "[Manufacturer]\n%Vendor%=Models,NTamd64\n" +
            "[Models.NTamd64]\n%Device%=Install, " + hardwareId;
        var entry = Assert.Single(InfModelEvidence.ReadDeclaredIds(inf));
        Assert.Equal(hardwareId.ToUpperInvariant(), entry.HardwareId);
    }

    [Theory]
    [InlineData(@"HID\..\driver.sys")]
    [InlineData(@"USB\VID_1234/../test")]
    [InlineData(@"UNKNOWN\VID_1234")]
    [InlineData(@"*MALICIOUS")]
    [InlineData(@"HID\name with spaces")]
    [InlineData(@"HID\")]
    public void UnknownMalformedOrPathLikeIdsAreNotIdentified(string hardwareId)
    {
        var inf = "[Manufacturer]\n%Vendor%=Models\n[Models]\n%Device%=Install, " +
            hardwareId;
        Assert.Empty(InfModelEvidence.ReadDeclaredIds(inf));
    }

    [Fact]
    public void RejectsExcessiveInput() =>
        Assert.Throws<InvalidDataException>(() =>
            InfModelEvidence.ReadDeclaredIds(new string('a', 1024 * 1024 + 1)));

    [Fact]
    public void SimilarButDifferentIdDoesNotMatch()
    {
        var ids = InfModelEvidence.ReadDeclaredIds(Inf.Replace("DEV_1234", "DEV_1235"));
        Assert.Equal(0, InfModelEvidence.MatchStrength(Device(), ids));
    }
}
