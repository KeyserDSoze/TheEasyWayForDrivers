using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class HardwareIdentityParserTests
{
    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_1234&SUBSYS_ABCDEF01", "Bus PCI", "8086", "1234", "ABCDEF01")]
    [InlineData(@"pci\ven_10de&dev_2484", "Bus PCI", "10DE", "2484", "Bus PCI")]
    [InlineData(@"USB\VID_046D&PID_C534", "Bus USB", "046D", "C534", "Bus USB")]
    public void Describe_ExtractsKnownBusIdentifiers(string hardwareId, string bus, string vendor, string device, string other)
    {
        var description = HardwareIdentityParser.Describe(hardwareId);
        Assert.Contains(bus, description);
        Assert.Contains(vendor, description);
        Assert.Contains(device, description);
        Assert.Contains(other, description);
    }

    [Fact]
    public void Describe_HandlesMissingHardwareId() =>
        Assert.Contains("non disponibile", HardwareIdentityParser.Describe(null));

    [Fact]
    public void Describe_HandlesAcpiIds() =>
        Assert.Contains("PNP0C09", HardwareIdentityParser.Describe(@"ACPI\PNP0C09"));

    [Theory]
     [InlineData("PCI\\VEN_00G0&DEV_1234")]
    [InlineData("PCI\\VEN_8086&DEV_1")]
    [InlineData("USB\\VID_046D&PID_????")]
    [InlineData("random-device")]
    public void Describe_RejectsIncompleteOrInvalidCodes(string value) =>
        Assert.Contains("non riconosciuto", HardwareIdentityParser.Describe(value));

    [Fact]
    public void Describe_DoesNotClaimCompatibleDriver() =>
        Assert.DoesNotContain("driver disponibile", HardwareIdentityParser.Describe(@"PCI\VEN_8086&DEV_1234"));
}
