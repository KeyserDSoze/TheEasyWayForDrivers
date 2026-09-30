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
}
