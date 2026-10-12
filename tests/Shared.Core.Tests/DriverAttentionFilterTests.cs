using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverAttentionFilterTests
{
    private static DriverInfo Device(bool hasDriver, uint code) =>
        new("PCI\\TEST", "Test device", "Vendor", "System", null, null,
            null, null, false, hasDriver, code, [], []);

    [Fact]
    public void HardwareProblems_ExcludesUnverifiedDeviceWithoutPnpCode()
    {
        var driver = Device(false, 0);
        Assert.True(DriverAttentionFilter.Matches(driver, false, "attention"));
        Assert.True(DriverAttentionFilter.Matches(driver, false, "Da verificare"));
        Assert.False(DriverAttentionFilter.Matches(driver, false, "hardware-problems"));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(22)]
    [InlineData(28)]
    [InlineData(43)]
    public void HardwareProblems_IncludeWindowsFaults(uint code) =>
        Assert.True(DriverAttentionFilter.Matches(Device(true, code), false, "hardware-problems"));

    [Fact]
    public void Attention_IncludesDeviceWithUpdateButNoPnpFault()
    {
        var driver = Device(true, 0);
        Assert.True(DriverAttentionFilter.Matches(driver, true, "attention"));
        Assert.False(DriverAttentionFilter.Matches(driver, true, "hardware-problems"));
        Assert.True(DriverAttentionFilter.Matches(driver, true, "Aggiornamento disponibile"));
    }
}
