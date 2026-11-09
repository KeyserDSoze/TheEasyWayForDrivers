using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class PciClassIdentityTests
{
    private static DriverInfo Device(string hardware,
        string? pciClass = null, string? pnpClass = "Unknown") =>
        new("PRIVATE_INSTANCE", "PCI Device", "Microsoft", pnpClass,
            "Microsoft", "1.0", null, "oem42.inf", true, true, 0,
            [hardware], pciClass is null ? [] : [pciClass]);

    [Theory]
    [InlineData(@"PCI\CC_0300")]
    [InlineData(@"PCI\CC_030200")]
    [InlineData(@"PCI\VEN_10DE&CC_0380")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&CC_030000")]
    public void NVIDIAWithValidPciDisplayClassIsRecognized(string cc)
    {
        var d = Device(@"PCI\VEN_10DE&DEV_28A0", cc);
        Assert.True(PciClassIdentity.HasDisplayClass(d, "10DE"));
        Assert.False(PciClassIdentity.HasDisplayClass(d, "8086"));
        Assert.False(PciClassIdentity.HasNetworkClass(d, "10DE"));
    }

    [Theory]
    [InlineData(@"PCI\CC_0280")]
    [InlineData(@"PCI\CC_020000")]
    [InlineData(@"PCI\VEN_8086&CC_028000")]
    public void IntelWithValidNetworkClassIsRecognized(string cc)
    {
        Assert.True(PciClassIdentity.HasNetworkClass(
            Device(@"PCI\VEN_8086&DEV_51F1", cc), "8086"));
    }

    [Theory]
    [InlineData(@"PCI\CC_03000")]
    [InlineData(@"PCI\CC_0300BAD")]
    [InlineData(@"PCI\CC_0300ZZ")]
    [InlineData(@"PCI\CC_03")]
    [InlineData(@"PCI\CC_0280")]
    [InlineData(@"USB\VID_10DE&PID_0300")]
    [InlineData(@"PCI\VEN_8086&CC_030000")]
    public void WrongClassOrVendorNeverMarksNvidiaPciDisplay(string cc)
    {
        Assert.False(PciClassIdentity.HasDisplayClass(
            Device(@"PCI\VEN_10DE&DEV_28A0", cc), "10DE"));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DEF&DEV_28A0")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A00")]
    [InlineData(@"PCI\VEN_10DE&DEV_28AZ")]
    [InlineData(@"PCI\VEN_8086&DEV_28A0")]
    [InlineData(@"ROOT\NVIDIA")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\INSTANCE")]
    public void NearMissVendorModelOrInstanceCannotProveDisplay(string id)
    {
        Assert.False(PciClassIdentity.HasDisplayClass(
            Device(id, @"PCI\CC_030000"), "10DE"));
    }

    [Theory]
    [InlineData("Camera")]
    [InlineData("Net")]
    [InlineData("Bluetooth")]
    [InlineData("Display")]
    public void ExistingSpecificPnpClassIsNeverOverridden(string pnpClass)
    {
        Assert.False(PciClassIdentity.HasDisplayClass(
            Device(@"PCI\VEN_10DE&DEV_28A0", @"PCI\CC_030200",
                pnpClass), "10DE"));
    }

    [Fact]
    public void ClassWithoutSameDeviceVendorIsNotVendorIdentity()
    {
        Assert.False(PciClassIdentity.HasDisplayClass(
            Device(@"PCI\CC_030000", @"PCI\CC_0300"), "10DE"));
        Assert.False(PciClassIdentity.HasNetworkClass(
            Device(@"PCI\VEN_168C&DEV_003E",
                @"PCI\VEN_14E4&CC_0280"), "168C"));
    }
}
