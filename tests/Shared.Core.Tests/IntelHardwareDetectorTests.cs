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
}
