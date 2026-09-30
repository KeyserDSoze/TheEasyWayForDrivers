using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class ReleaseAssetNamePolicyTests
{
    [Fact]
    public void PreferredSetupAssetNames_PrefersOmegaDrive()
    {
        Assert.Equal(
            "OmegaDrive-Setup.exe",
            ReleaseAssetNamePolicy
                .PreferredSetupAssetNames[0]);
    }

    [Fact]
    public void PreferredSetupAssetNames_KeepsLegacyAliasForMigration()
    {
        Assert.Contains(
            "TheEasyWayForDrivers-Setup.exe",
            ReleaseAssetNamePolicy
                .PreferredSetupAssetNames);
    }
}
