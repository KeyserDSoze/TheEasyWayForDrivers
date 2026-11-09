using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class AmdHardwareDetectorTests
{
    [Fact]
    public void IsAmdDevice_DetectsVendor1002()
    {
        Assert.True(
            AmdHardwareDetector.IsAmdDevice(
                CreateDriver(
                    [@"PCI\VEN_1002&DEV_744C"])));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10020&DEV_744C")]
    [InlineData(@"PCI\VEN_1002FAKE&DEV_744C")]
    public void IsAmdDevice_RejectsNearMissVendorIds(string hardwareId)
    {
        Assert.False(AmdHardwareDetector.IsAmdDevice(
            CreateDriver([hardwareId])));
    }

    [Fact]
    public void IsAmdDevice_UsesRadeonMetadataAsFallback()
    {
        Assert.True(
            AmdHardwareDetector.IsAmdDevice(
                CreateDriver(
                    [],
                    "AMD Radeon")));
    }

    [Fact]
    public void IsAmdDevice_RejectsOtherVendor()
    {
        Assert.False(
            AmdHardwareDetector.IsAmdDevice(
                CreateDriver(
                    [@"PCI\VEN_10DE&DEV_2684"],
                    "NVIDIA")));
    }

    private static DriverInfo CreateDriver(
        IReadOnlyList<string> hardwareIds,
        string? manufacturer = null) =>
        new(
            @"PCI\TEST\1",
            "Test device",
            manufacturer,
            "Display",
            manufacturer,
            "1.0",
            null,
            "test.inf",
            true,
            true,
            0,
            hardwareIds,
            []);

    [Theory]
    [InlineData(@"PCI\VEN_1002")]
    [InlineData(@"PCI\VEN_1002&FAKE_744C")]
    [InlineData(@"PCI\VEN_1002&DEV_")]
    [InlineData(@"PCI\VEN_1002&DEV_744")]
    [InlineData(@"PCI\VEN_1002&DEV_744C\PRIVATE_INSTANCE")]
    public void IncompleteAmdPciTokensDoNotCreateVendorEvidence(string id)
    {
        var device = CreateDriver([id], "Microsoft") with
        {
            Name = "Generic Windows Device",
            DeviceClass = "Unknown",
            DriverProvider = "Microsoft"
        };
        Assert.False(AmdHardwareDetector.IsAmdDevice(device));
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
    }

    [Theory]
    [InlineData(@"PCI\VEN_1002&DEV_744C&SUBSYS_12341002&REV_01")]
    [InlineData(@"PCI\VEN_1002&DEV_744C&CC_030000")]
    public void ValidAmdPciModelRemainsRecognized(string id)
    {
        var device = CreateDriver([id], "Microsoft") with
        {
            Name = "Generic Windows Device",
            DeviceClass = "Unknown",
            DriverProvider = "Microsoft"
        };
        Assert.True(AmdHardwareDetector.IsAmdDevice(device));
    }

}
