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
    public void FindMatches_PrefersExactHardwareIdOverNewerCompatibleId()
    {
        var driver = CreateDriver(
            [@"PCI\VEN_8086&DEV_1234&SUBSYS_0001"],
            [@"PCI\VEN_8086&DEV_1234"]);

        var exact = CreateUpdate(
            @"PCI\VEN_8086&DEV_1234&SUBSYS_0001",
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "Exact");
        var compatible = CreateUpdate(
            @"PCI\VEN_8086&DEV_1234",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "Compatible");

        var matches = DriverUpdateMatcher.FindMatches(driver, [compatible, exact]);
        Assert.Equal("Exact", matches[0].Title);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(driver, exact));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(driver, compatible));
    }

    [Fact]
    public void MatchStrength_DoesNotMatchMissingOrDifferentIds()
    {
        var driver = CreateDriver([@"PCI\VEN_8086&DEV_1234"], []);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(driver, CreateUpdate("")));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(driver, CreateUpdate(@"PCI\VEN_8086&DEV_9999")));
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
