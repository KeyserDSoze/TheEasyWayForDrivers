using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class IntelHardwareDetectorTests
{
    [Fact]
    public void IsIntelDevice_DetectsPciVendor8086()
    {
        var driver = CreateDriver(
            hardwareIds: [@"PCI\VEN_8086&DEV_46A8"]);

        Assert.True(IntelHardwareDetector.IsIntelDevice(driver));
    }

    [Fact]
    public void IsIntelDevice_DetectsUsbVendor8087()
    {
        var driver = CreateDriver(
            hardwareIds: [@"USB\VID_8087&PID_0033"]);

        Assert.True(IntelHardwareDetector.IsIntelDevice(driver));
    }

    [Theory]
    [InlineData(@"PCI\VEN_80860&DEV_1234")]
    [InlineData(@"USB\VID_80870&PID_1234")]
    [InlineData(@"PCI\VEN_8086X&DEV_1234")]
    public void IsIntelDevice_RejectsLookalikeVendorPrefixes(string hardwareId)
    {
        var driver = CreateDriver(hardwareIds: [hardwareId]);
        Assert.False(IntelHardwareDetector.IsIntelDevice(driver));
    }

    [Fact]
    public void IsIntelDevice_UsesManufacturerAsFallback()
    {
        var driver = CreateDriver(
            manufacturer: "Intel Corporation");

        Assert.True(IntelHardwareDetector.IsIntelDevice(driver));
    }

    [Fact]
    public void IsIntelDevice_RejectsOtherVendor()
    {
        var driver = CreateDriver(
            hardwareIds: [@"PCI\VEN_10DE&DEV_2684"],
            manufacturer: "NVIDIA");

        Assert.False(IntelHardwareDetector.IsIntelDevice(driver));
    }

    [Fact]
    public void CountIntelDevices_CountsOnlyApplicableDevices()
    {
        var intel = CreateDriver(
            hardwareIds: [@"PCI\VEN_8086&DEV_1234"]);

        var other = CreateDriver(
            hardwareIds: [@"PCI\VEN_10DE&DEV_5678"],
            manufacturer: "NVIDIA");

        Assert.Equal(
            1,
            IntelHardwareDetector.CountIntelDevices(
                [intel, other]));
    }

    private static DriverInfo CreateDriver(
        IReadOnlyList<string>? hardwareIds = null,
        IReadOnlyList<string>? compatibleIds = null,
        string? manufacturer = null,
        string? provider = null) =>
        new(
            @"PCI\TEST\1",
            "Test device",
            manufacturer,
            "System",
            provider,
            "1.0.0.0",
            DateTimeOffset.UtcNow,
            "test.inf",
            true,
            true,
            0,
            hardwareIds ?? [],
            compatibleIds ?? []);

    [Theory]
    [InlineData(@"PCI\VEN_8086")]
    [InlineData(@"PCI\VEN_8086&FAKE_1234")]
    [InlineData(@"PCI\VEN_8086&DEV_")]
    [InlineData(@"PCI\VEN_8086&DEV_12G4")]
    [InlineData(@"PCI\VEN_8086&DEV_1234\PRIVATE_INSTANCE")]
    [InlineData(@"USB\VID_8087")]
    [InlineData(@"USB\VID_8087&FAKE_0033")]
    [InlineData(@"USB\VID_8087&PID_")]
    [InlineData(@"USB\VID_8087&PID_0033&UID_01")]
    public void IncompleteIntelPciUsbTokensAreNotEvidence(string id)
    {
        var device = CreateDriver(
            hardwareIds: [id], manufacturer: "Microsoft", provider: "Microsoft");
        Assert.False(IntelHardwareDetector.IsIntelDevice(device));
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(
            device with { Name = "Generic Windows Device", DeviceClass = "Unknown" }));
    }

    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_46A8&SUBSYS_12348086&REV_01")]
    [InlineData(@"PCI\VEN_8086&DEV_46A8&CC_030000")]
    [InlineData(@"USB\VID_8087&PID_0033&REV_0100")]
    [InlineData(@"USB\VID_8087&PID_0033&MI_00")]
    public void ValidIntelPciAndUsbModelsRemainRecognized(string id) =>
        Assert.True(IntelHardwareDetector.IsIntelDevice(CreateDriver(
            hardwareIds: [id], manufacturer: "Microsoft", provider: "Microsoft")));

}
