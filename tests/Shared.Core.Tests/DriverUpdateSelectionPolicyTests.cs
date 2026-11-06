using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverUpdateSelectionPolicyTests
{
    [Fact]
    public void ShouldSelectByDefault_SelectsNormalUpdate()
    {
        Assert.True(
            DriverUpdateSelectionPolicy.ShouldSelectByDefault(
                CreateUpdate()));
    }

    [Fact]
    public void ShouldSelectByDefault_DoesNotSelectAdvancedCandidate()
    {
        Assert.False(
            DriverUpdateSelectionPolicy.ShouldSelectByDefault(
                CreateUpdate(
                    isAdvancedCandidate: true)));
    }

    [Fact]
    public void ShouldSelectByDefault_DoesNotSelectHiddenUpdate()
    {
        Assert.False(
            DriverUpdateSelectionPolicy.ShouldSelectByDefault(
                CreateUpdate(
                    isHidden: true)));
    }

    private static DriverUpdateInfo CreateUpdate(
        bool isHidden = false,
        bool isAdvancedCandidate = false) =>
        new(
            Guid.NewGuid().ToString("D"),
            "Driver",
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            IsHidden: isHidden,
            IsAdvancedCandidate: isAdvancedCandidate);
    [Theory]
    [InlineData("NVIDIA Display", "Display", "Windows Update online", true)]
    [InlineData("Intel Net", "Net", "Microsoft Update online", true)]
    [InlineData("BIOS update", "System", "Windows Update online", false)]
    [InlineData("UEFI update", "System", "Windows Update online", false)]
    [InlineData("Generic driver", "Firmware", "Windows Update online", false)]
    [InlineData("NVIDIA Display", "Display", "Untrusted OEM website", false)]
    [InlineData("NVIDIA Display", "Display", "Windows Update online · avanzato", false)]
    public void DefaultSelectionAgreesWithQuickInstallPolicy(
        string title, string driverClass, string source, bool expected)
    {
        var update = CreateUpdate() with
        {
            Title = title,
            DriverClass = driverClass,
            SearchSource = source
        };
        Assert.Equal(expected, DriverUpdateSelectionPolicy.ShouldSelectByDefault(update));
        Assert.Equal(expected, QuickUpdatePolicy.IsEligible(update));
    }

    [Fact]
    public void InvalidGuidCanNeverBeDefaultSelected()
    {
        var invalid = CreateUpdate() with { Id = "not-a-wua-guid" };
        Assert.False(DriverUpdateSelectionPolicy.ShouldSelectByDefault(invalid));
    }

}
