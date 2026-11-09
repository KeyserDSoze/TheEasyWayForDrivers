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

    [Fact]
    public void UnknownPnpProblemCodeIsNotHealthyMissingOrConfirmedWindowsError()
    {
        var nvidia = CreateDriver(hasDriver: true, errorCode: 0) with
        {
            DeviceId = @"PCI\\VEN_10DE&DEV_28A0\\PRIVATE_SERIAL",
            PnpProblemCodeVerified = false
        };

        Assert.False(nvidia.IsDriverMissing);
        Assert.False(nvidia.HasWindowsProblem);
        Assert.True(nvidia.IsDriverUnverified);
        Assert.Equal("Da verificare", nvidia.Status);
        Assert.Contains("non verificato", nvidia.StatusDetail);
        Assert.False(nvidia.NeedsAttention);
        Assert.Contains("Codice problema PnP Windows non verificato",
            TheEasyWayForDrivers.Core.Drivers.DriverRemediationAdvisor
                .GetNextAction(nvidia, false, true, "NVIDIA"));
        Assert.DoesNotContain("PRIVATE_SERIAL", nvidia.StatusDetail);

        // Even a synthetic nonzero code is NOT trusted if marked unknown.
        var invalid = nvidia with { ConfigManagerErrorCode = 28 };
        Assert.False(invalid.IsDriverMissing);
        Assert.False(invalid.HasWindowsProblem);
        Assert.Equal("Da verificare", invalid.Status);
    }

    [Fact]
    public void NewPnpProblemCodeVerificationFlagDefaultsTrueForOldIpcJson()
    {
        var driver = CreateDriver(hasDriver: true, errorCode: 0);
        var json = System.Text.Json.JsonSerializer.Serialize(driver);
        Assert.Contains("\"PnpProblemCodeVerified\":true", json);
        var legacy = json.Replace(
            ",\"PnpProblemCodeVerified\":true", "", StringComparison.Ordinal);
        Assert.NotEqual(json, legacy);
        var imported = System.Text.Json.JsonSerializer.Deserialize<DriverInfo>(legacy);
        Assert.NotNull(imported);
        Assert.True(imported.PnpProblemCodeVerified);
        Assert.Equal("OK", imported.Status);
    }

}
