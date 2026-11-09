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
    [InlineData(@"ACPI\NVDA0820&REV_01")]
    [InlineData(@"SWC\VEN_10DE&DEV_28A0")]
    [InlineData(@"SWC\VEN_10DE&DEV_28A0&REV_01")]
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
    [InlineData(@"ACPI\NVDA082")]
    [InlineData(@"ACPI\NVDA0820XYZ")]
    [InlineData(@"ACPI\NVDAZZZZ")]
    [InlineData(@"SWC\VEN_10DEF&DEV_28A0")]
    [InlineData(@"SWC\VEN_10DE&DEV_")]
    [InlineData(@"SWC\VEN_10DE&DEV_28A")]
    [InlineData(@"SWC\VEN_10DE&DEV_28A0XYZ")]
    [InlineData(@"SWC\VEN_10DE&DEV_XXXX")]
    [InlineData(@"PCI\VEN_10DEF&DEV_0001")]
    public void RejectsAmbiguousNvidiaPrefixes(string id) =>
        Assert.False(NvidiaHardwareDetector.IsNvidiaDevice(CreateDriver([id])));

    [Fact]
    public void PlatformRecognitionUsesHardwareOrCompatibleIdsWithoutNames()
    {
        var hardware = CreateDriver([@"SWC\VEN_10DE&DEV_28A0"]) with
        {
            Name = "Software Component",
            DeviceClass = "SoftwareComponent",
            Manufacturer = "Microsoft",
            DriverProvider = "Microsoft"
        };
        var compatible = CreateDriver([@"ROOT\SOFTWARE"]) with
        {
            Name = "System Controller",
            DeviceClass = "System",
            CompatibleIds = [@"ACPI\NVDA0820"],
            Manufacturer = "Microsoft",
            DriverProvider = "Microsoft"
        };
        Assert.True(NvidiaHardwareDetector.HasNvidiaPlatformId(hardware));
        Assert.True(NvidiaHardwareDetector.HasNvidiaPlatformId(compatible));
        Assert.True(NvidiaHardwareDetector.IsNvidiaDevice(hardware));
        Assert.True(NvidiaHardwareDetector.IsNvidiaDevice(compatible));

        var other = hardware with
        {
            HardwareIds = [@"SWC\VEN_10DE&DEV_28A0XYZ"]
        };
        Assert.False(NvidiaHardwareDetector.HasNvidiaPlatformId(other));
        Assert.False(NvidiaHardwareDetector.IsNvidiaDevice(other));
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
