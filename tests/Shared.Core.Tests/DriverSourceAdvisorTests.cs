using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverSourceAdvisorTests
{
    [Fact]
    public void GetSource_PrefersExactWindowsUpdateMatch()
    {
        var driver = CreateDriver(@"PCI\VEN_10DE&DEV_2684");
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
                [update]));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_2684", "NVIDIA")]
    [InlineData(@"PCI\VEN_1002&DEV_744C", "AMD")]
    [InlineData(@"PCI\VEN_8086&DEV_46A8", "Intel")]
    [InlineData(@"PCI\VEN_1234&DEV_5678", "Windows / OEM")]
    public void GetSource_UsesHardwareVendorWithoutWindowsUpdateMatch(
        string hardwareId,
        string expected)
    {
        Assert.Equal(
            expected,
            DriverSourceAdvisor.GetSource(
                CreateDriver(hardwareId),
                []));
    }

    private static DriverInfo CreateDriver(
        string hardwareId) =>
        new(
            hardwareId,
            "Test device",
            null,
            "Display",
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
