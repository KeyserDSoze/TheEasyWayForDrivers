using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverSourceAdvisorTests
{
    [Fact]
    public void GetSource_PrefersExactWindowsUpdateMatch()
    {
        var driver = CreateDriver(
            @"PCI\VEN_10DE&DEV_2684",
            "Display");

        var update = new DriverUpdateInfo(
            Guid.NewGuid().ToString(),
            "Matched update",
            null,
            "Display",
            "NVIDIA",
            null,
            100,
            false,
            "NVIDIA",
            "GPU",
            @"PCI\VEN_10DE&DEV_2684",
            DateTimeOffset.UtcNow);

        Assert.Equal(
            "Windows Update",
            DriverSourceAdvisor.GetSource(
                driver,
                [update],
                "Dell"));
    }

    [Fact]
    public void GetSource_UsesSystemOemForFirmware()
    {
        Assert.Equal(
            "Dell (OEM PC)",
            DriverSourceAdvisor.GetSource(
                CreateDriver(
                    @"ACPI\DELL0001",
                    "Firmware"),
                [],
                "Dell"));
    }

    [Fact]
    public void GetSource_ShowsChipVendorAndOemForInternalGpu()
    {
        Assert.Equal(
            "NVIDIA · Dell OEM",
            DriverSourceAdvisor.GetSource(
                CreateDriver(
                    @"PCI\VEN_10DE&DEV_2684",
                    "Display"),
                [],
                "Dell"));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_2684", "NVIDIA")]
    [InlineData(@"PCI\VEN_1002&DEV_744C", "AMD")]
    [InlineData(@"PCI\VEN_8086&DEV_46A8", "Intel")]
    [InlineData(@"PCI\VEN_1234&DEV_5678", "Windows / OEM")]
    public void GetSource_UsesHardwareVendorWithoutSystemOem(
        string hardwareId,
        string expected)
    {
        Assert.Equal(
            expected,
            DriverSourceAdvisor.GetSource(
                CreateDriver(
                    hardwareId,
                    "Display"),
                []));
    }

    private static DriverInfo CreateDriver(
        string hardwareId,
        string deviceClass) =>
        new(
            hardwareId,
            "Test device",
            null,
            deviceClass,
            null,
            "1.0",
            null,
            "test.inf",
            true,
            true,
            0,
            [hardwareId],
            []);
}
