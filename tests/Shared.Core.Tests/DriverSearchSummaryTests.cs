using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverSearchSummaryTests
{
    [Fact]
    public void Create_GroupsSearchResultsAndOemSources()
    {
        var updates = new[]
        {
            CreateUpdate("recommended"),
            CreateUpdate("optional", isOptional: true),
            CreateUpdate("advanced", isAdvanced: true),
            CreateUpdate("hidden", isHidden: true)
        };

        var summary =
            DriverSearchSummary.Create(
                updates,
                oemSources: 3);

        Assert.Equal(1, summary.Recommended);
        Assert.Equal(1, summary.Optional);
        Assert.Equal(2, summary.Advanced);
        Assert.Equal(3, summary.OemSources);
    }

    private static DriverUpdateInfo CreateUpdate(
        string id,
        bool isOptional = false,
        bool isAdvanced = false,
        bool isHidden = false) =>
        new(
            id,
            id,
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
            IsOptional: isOptional,
            IsAdvancedCandidate: isAdvanced);
}
