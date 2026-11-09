using TheEasyWayForDrivers.Core.Update;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

public sealed class WuaInstallRecheckPolicyTests
{
    private static readonly string GuidId = Guid.NewGuid().ToString("D");

    [Fact]
    public void RecheckUsesOnlyRecommendedNonSupersededDriverQuery()
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
    public void OrdinaryVisibleNonBrowseOnlyWuaDriverStaysEligible(
        string title, string driverClass)
    {
        Assert.True(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId.ToUpperInvariant(), false, false,
            title, driverClass));
    }

    [Theory]
    [InlineData("BIOS update", "System")]
    [InlineData("UEFI system update", "System")]
    [InlineData("NVIDIA - Firmware", "System")]
    [InlineData("Intel chipset update", "Firmware")]
    [InlineData("Generic firmware version", "Display")]
    [InlineData("UEFI", "Extension")]
    public void FirmwareCannotPassPrivilegedGate(
        string title, string driverClass)
    {
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId, false, false, title, driverClass));
        Assert.True(QuickUpdatePolicy.IsFirmwareOrBios(title, driverClass));
    }

    [Fact]
    public void BrowseOnlyHiddenAndUnknownFlagsFailClosed()
    {
        foreach (var (hidden, browseOnly) in new (bool?, bool?)[]
        {
            (true, false), (false, true), (true, true),
            (null, false), (false, null), (null, null)
        })
        {
            Assert.False(WuaInstallRecheckPolicy.CanStage(
                GuidId, GuidId, hidden, browseOnly,
                "Intel Killer - Net", "Net"));
        }
    }

    [Fact]
    public void WrongGuidOrMissingTitleOrClassCannotStage()
    {
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, Guid.NewGuid().ToString("D"), false, false,
            "NVIDIA - Display", "Display"));
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            "not-guid", GuidId, false, false,
            "NVIDIA - Display", "Display"));
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, "nvidia.example.com", false, false,
            "NVIDIA - Display", "Display"));
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId, false, false, null, "Display"));
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId, false, false, "", "Display"));
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId, false, false, "NVIDIA - Display", null));
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId, false, false, "NVIDIA - Display", ""));
    }

    [Theory]
    [InlineData("NVIDIA\nPRIVATE", "Display")]
    [InlineData("NVIDIA\u202Eprivate", "Display")]
    [InlineData("NVIDIA - Display", "MEDIA\0PRIVATE")]
    public void ControlOrFormatCharactersCanNeverStage(
        string title, string driverClass)
    {
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId, false, false, title, driverClass));
    }

    [Fact]
    public void TitleOrClassOverComBudgetCannotStage()
    {
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId, false, false,
            new string('A', WuaDriverCandidateReader.MaximumTitleLength + 1),
            "Display"));
        Assert.False(WuaInstallRecheckPolicy.CanStage(
            GuidId, GuidId, false, false, "Realtek - MEDIA",
            new string('A', WuaDriverCandidateReader.MaximumClassLength + 1)));
    }

    [Fact]
    public void NoAlternateFirmwareOrAdvancedSearchCriteriaUsedForInstallation()
    {
        Assert.NotEqual(WindowsDriverSearchPlan.BroadCriteria,
            WuaInstallRecheckPolicy.Criteria);
        Assert.Equal(
            "IsInstalled=0 and IsHidden=0 and Type='Driver'",
            WuaInstallRecheckPolicy.Criteria);
    }
}
