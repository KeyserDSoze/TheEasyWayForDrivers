using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverUpdateMatcherTests
{
    [Fact]
    public void IsMatch_MatchesExactHardwareIdIgnoringCase()
    {
        var driver = CreateDriver(
            hardwareIds: [@"PCI\VEN_8086&DEV_1234"],
            compatibleIds: []);

        var update = CreateUpdate(@"pci\ven_8086&dev_1234");

        Assert.True(DriverUpdateMatcher.IsMatch(driver, update));
    }

    [Fact]
    public void IsMatch_MatchesCompatibleId()
    {
        var driver = CreateDriver(
            hardwareIds: [@"PCI\VEN_8086&DEV_1234&SUBSYS_0001"],
            compatibleIds: [@"PCI\VEN_8086&DEV_1234"]);

        var update = CreateUpdate(@"PCI\VEN_8086&DEV_1234");

        Assert.True(DriverUpdateMatcher.IsMatch(driver, update));
    }

    [Fact]
    public void IsMatch_DoesNotGuessFromDeviceName()
    {
        var driver = CreateDriver(
            hardwareIds: [@"PCI\VEN_8086&DEV_1111"],
            compatibleIds: []);

        var update = CreateUpdate(@"PCI\VEN_8086&DEV_2222");

        Assert.False(DriverUpdateMatcher.IsMatch(driver, update));
    }

    [Fact]
    public void FindMatches_OrdersNewestDriverDateFirst()
    {
        var driver = CreateDriver(
            hardwareIds: [@"PCI\VEN_8086&DEV_1234"],
            compatibleIds: []);

        var older = CreateUpdate(
            @"PCI\VEN_8086&DEV_1234",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "Older");

        var newer = CreateUpdate(
            @"PCI\VEN_8086&DEV_1234",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "Newer");

        var matches = DriverUpdateMatcher.FindMatches(
            driver,
            [older, newer]);

        Assert.Equal("Newer", matches[0].Title);
    }

    private static DriverInfo CreateDriver(
        IReadOnlyList<string> hardwareIds,
        IReadOnlyList<string> compatibleIds) =>
        new(
            @"PCI\VEN_8086&DEV_1234\1",
            "Intel test device",
            "Intel",
            "System",
            "Intel",
            "1.0.0.0",
            DateTimeOffset.UtcNow,
            "test.inf",
            true,
            true,
            0,
            hardwareIds,
            compatibleIds);

    private static DriverUpdateInfo CreateUpdate(
        string hardwareId,
        DateTimeOffset? date = null,
        string title = "Intel update") =>
        new(
            Guid.NewGuid().ToString(),
            title,
            null,
            "System",
            "Intel",
            null,
            1024,
            false,
            "Intel",
            "Test model",
            hardwareId,
            date);
}
