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

    [Fact]
    public void EligibilityDoesNotInferVendorUpdateFromMissingWuaMatch()
    {
        var driverUpdate = Update(Guid.NewGuid().ToString("D"));
        Assert.True(QuickUpdatePolicy.IsEligible(driverUpdate));
        Assert.False(QuickUpdatePolicy.IsEligible(
            Update("https://nvidia.example.org/driver.exe")));
    }
}
