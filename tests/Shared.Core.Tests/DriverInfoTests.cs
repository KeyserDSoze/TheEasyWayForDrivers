using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverInfoTests
{
    [Fact]
    public void Status_IsOk_WhenDriverExistsAndWindowsReportsNoError()
    {
        var driver = CreateDriver(hasDriver: true, errorCode: 0);

        Assert.False(driver.NeedsAttention);
        Assert.Equal("OK", driver.Status);
    }

    [Fact]
    public void Status_IsMissing_WhenNoSignedDriverIsAssociated()
    {
        var driver = CreateDriver(hasDriver: false, errorCode: 28);

        Assert.True(driver.NeedsAttention);
        Assert.Equal("Driver mancante", driver.Status);
    }

    [Fact]
    public void Status_IsWindowsError_WhenDriverExistsButDeviceHasProblem()
    {
        var driver = CreateDriver(hasDriver: true, errorCode: 10);

        Assert.True(driver.NeedsAttention);
        Assert.Equal("Errore Windows", driver.Status);
        Assert.Contains("10", driver.StatusDetail);
    }

    private static DriverInfo CreateDriver(bool hasDriver, uint errorCode) =>
        new(
            "PCI\\VEN_TEST",
            "Test device",
            "Test vendor",
            "System",
            "Test provider",
            "1.0.0",
            DateTimeOffset.UtcNow,
            "test.inf",
            true,
            hasDriver,
            errorCode);
}
