using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class QuickUpdatePolicyTests
{
    private static DriverUpdateInfo Update(
        string id,
        bool hidden = false,
        bool advanced = false,
        bool optional = false) =>
        new(id, "Test driver", null, null, "Microsoft", null, null, false,
            null, null, null, null, hidden, optional, advanced);

    [Fact]
    public void AllOrdinaryIncludesWuaRecommendedAndOptionalOnce()
    {
        var id = Guid.NewGuid().ToString("D");
        var otherId = Guid.NewGuid().ToString("D");
        var result = QuickUpdatePolicy.AllOrdinaryIds([
            Update(id), Update(id.ToUpperInvariant()),
            Update(otherId, optional: true)
        ]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void HiddenAdvancedAndUnrecognizedIdsNeverEnterQuickInstall()
    {
        var result = QuickUpdatePolicy.AllOrdinaryIds([
            Update(Guid.NewGuid().ToString("D"), hidden: true),
            Update(Guid.NewGuid().ToString("D"), advanced: true),
            Update("vendor-non-wua-package")
        ]);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("Windows configurato", true)]
    [InlineData("Windows Update online", true)]
    [InlineData("Microsoft Update online", true)]
    [InlineData("MICROSOFT UPDATE ONLINE", true)]
    [InlineData("Windows Update", true)]
    [InlineData("Microsoft Update online · avanzato", false)]
    [InlineData("Microsoft Update online — fake", false)]
    [InlineData("NVIDIA official", false)]
    [InlineData("Dell", false)]
    [InlineData("Microsoft Update catalog", false)]
    [InlineData("", false)]
    public void QuickInstallAcceptsOnlyExactWuaProviderNames(string source, bool expected)
    {
        var update = Update(Guid.NewGuid().ToString("D")) with
        {
            SearchSource = source
        };
        Assert.Equal(expected, QuickUpdatePolicy.IsEligible(update));
    }

    [Fact]
    public void MuRecommendedVisibleButAdvancedHiddenFirmwareAndInvalidIdAreNotInstallable()
    {
        var id = Guid.NewGuid().ToString("D");
        var safe = Update(id) with { SearchSource = "Microsoft Update online" };
        var advanced = Update(Guid.NewGuid().ToString("D"), advanced: true) with
        {
            SearchSource = "Microsoft Update online · avanzato"
        };
        var hidden = Update(Guid.NewGuid().ToString("D"), hidden: true) with
        {
            SearchSource = "Microsoft Update online"
        };
        var firmware = Update(Guid.NewGuid().ToString("D")) with
        {
            Title = "UEFI BIOS Firmware", SearchSource = "Microsoft Update online"
        };
        var forged = Update("nvidia-driver-url") with
        {
            SearchSource = "Microsoft Update online"
        };
        Assert.Equal([id], QuickUpdatePolicy.AllOrdinaryIds(
            [safe, advanced, hidden, firmware, forged]));
    }

    [Fact]
    public void EligibilityDoesNotInferVendorUpdateFromMissingWuaMatch()
    {
        var driverUpdate = Update(Guid.NewGuid().ToString("D"));
        Assert.True(QuickUpdatePolicy.IsEligible(driverUpdate));
        Assert.False(QuickUpdatePolicy.IsEligible(
            Update("https://nvidia.example.org/driver.exe")));
    }
}
