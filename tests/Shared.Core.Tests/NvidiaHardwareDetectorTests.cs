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

    [Theory]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    [InlineData(@"ACPI\NVDA0820")]
    public void DetectsGenericNvidiaAudioAndPlatformIds(string id) =>
        Assert.True(NvidiaHardwareDetector.IsNvidiaDevice(CreateDriver([id])));

    [Fact]
    public void DetectsNvidiaInCompatibleIds()
    {
        var driver = CreateDriver([@"PCI\VEN_1234&DEV_0001"]) with
        {
            CompatibleIds = [@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B"]
        };
        Assert.True(NvidiaHardwareDetector.IsNvidiaDevice(driver));
    }

    [Theory]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DEC&DEV_009B")]
    [InlineData(@"ACPI\NVDA")]
    [InlineData(@"PCI\VEN_10DEF&DEV_0001")]
    public void RejectsAmbiguousNvidiaPrefixes(string id) =>
        Assert.False(NvidiaHardwareDetector.IsNvidiaDevice(CreateDriver([id])));

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
