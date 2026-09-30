using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class NvidiaHardwareDetectorTests
{
    [Fact]
    public void IsNvidiaDevice_DetectsVendor10De()
    {
        Assert.True(
            NvidiaHardwareDetector.IsNvidiaDevice(
                CreateDriver(
                    [@"PCI\VEN_10DE&DEV_2684"])));
    }

    [Fact]
    public void IsNvidiaDevice_RejectsOtherVendor()
    {
        Assert.False(
            NvidiaHardwareDetector.IsNvidiaDevice(
                CreateDriver(
                    [@"PCI\VEN_8086&DEV_46A8"],
                    "Intel")));
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
