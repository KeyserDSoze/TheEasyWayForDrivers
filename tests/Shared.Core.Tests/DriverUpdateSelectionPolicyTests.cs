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
            "id",
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
}
