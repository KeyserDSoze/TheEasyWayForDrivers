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

    [Theory]
    [InlineData(10, "avviato")]
    [InlineData(22, "disabilitato")]
    [InlineData(28, "non installato")]
    [InlineData(43, "arrestato")]
    public void ProblemExplanation_DescribesKnownWindowsCodes(uint code, string phrase)
    {
        var driver = CreateDriver(true, code);
        Assert.True(driver.HasWindowsProblem);
        Assert.Contains(phrase, driver.ProblemExplanation);
    }

    [Fact]
    public void PrimaryHardwareId_ReturnsFirstId()
    {
        Assert.Equal(@"PCI\VEN_TEST&DEV_0001", CreateDriver(false, 28).PrimaryHardwareId);
    }

    [Fact]
    public void MissingSignedDriverWithNoPnpError_IsUnverifiedNotMissing()
    {
        var driver = CreateDriver(false, 0);
        Assert.True(driver.IsDriverUnverified);
        Assert.False(driver.IsDriverMissing);
        Assert.Equal("Da verificare", driver.Status);
    }

    [Fact]
    public void PnpCode28_IsMissingEvenWhenSignedMetadataExists()
    {
        var driver = CreateDriver(true, 28);
        Assert.True(driver.IsDriverMissing);
        Assert.Equal("Driver mancante", driver.Status);
    }

    [Fact]
    public void NoSignedDriverWithoutWindowsError_IsNotTreatedAsConfirmedCode28()
    {
        var driver = CreateDriver(false, 0);
        Assert.False(driver.HasWindowsProblem);
        Assert.Contains("verificare", driver.ProblemExplanation);
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
            errorCode,
            [@"PCI\VEN_TEST&DEV_0001"],
            [@"PCI\VEN_TEST"]);
}
