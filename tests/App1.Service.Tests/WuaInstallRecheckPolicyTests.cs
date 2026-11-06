using TheEasyWayForDrivers.Core.Update;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

public sealed class WuaInstallRecheckPolicyTests
{
    private static readonly string GuidId = Guid.NewGuid().ToString("D");

    [Fact]
    public void RecheckUsesNonHiddenNonSupersededDriverQueryOnly()
    {
        Assert.Equal(WindowsDriverSearchPlan.RecommendedCriteria,
            WuaInstallRecheckPolicy.Criteria);
        Assert.Contains("IsHidden=0", WuaInstallRecheckPolicy.Criteria);
        Assert.Contains("Type='Driver'", WuaInstallRecheckPolicy.Criteria);
        Assert.False(WuaInstallRecheckPolicy.IncludePotentiallySuperseded);
    }

    [Theory]
    [InlineData("NVIDIA - Display - 32.0.16.2010", "Display")]
    [InlineData("Intel - Net - 24.80.0.2", "Net")]
    [InlineData("Realtek - MEDIA - 6.0.1.0", "MEDIA")]
    [InlineData("Intel - Extension", "Extension")]
    public void OrdinaryNonHiddenWuaDriverRemainsEligible(
        string title, string driverClass)
    {
        Assert.True(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId.ToUpperInvariant(), false, title, driverClass));
    }

    [Theory]
    [InlineData("BIOS update", "System")]
    [InlineData("UEFI system update", "System")]
    [InlineData("NVIDIA - Firmware", "System")]
    [InlineData("Intel chipset update", "Firmware")]
    [InlineData("Generic firmware version", "Display")]
    [InlineData("UEFI", "Extension")]
    public void FirmwareNeverBecomesInstallableEvenWithWuaDriverType(
        string title, string driverClass)
    {
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId, false, title, driverClass));
        Assert.True(QuickUpdatePolicy.IsFirmwareOrBios(title, driverClass));
    }

    [Fact]
    public void HiddenOrMismatchedIdsFailBeforeAnyEulaOrDownload()
    {
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId, true, "NVIDIA - Display", "Display"));
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, Guid.NewGuid().ToString("D"), false,
            "NVIDIA - Display", "Display"));
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            "not-guid", GuidId, false, "NVIDIA - Display", "Display"));
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, "nvidia.example.com", false,
            "NVIDIA - Display", "Display"));
    }

    [Fact]
    public void MissingTitleFailsClosedRatherThanAssumingOrdinaryDriver()
    {
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId, false, null, "Display"));
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId, false, "", "Display"));
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId, false, "  ", "Display"));
    }

    [Fact]
    public void NoAlternateFirmwareOrAdvancedSearchCriteriaAreUsedForInstallation()
    {
        Assert.NotEqual(WindowsDriverSearchPlan.BroadCriteria,
            WuaInstallRecheckPolicy.Criteria);
        Assert.Equal(
            "IsInstalled=0 and IsHidden=0 and Type='Driver'",
            WuaInstallRecheckPolicy.Criteria);
    }
}
