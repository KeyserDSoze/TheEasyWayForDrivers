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
