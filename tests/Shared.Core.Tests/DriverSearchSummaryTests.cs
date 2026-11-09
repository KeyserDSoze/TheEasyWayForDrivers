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
            Guid.NewGuid().ToString("D"),
            id,
            null,
            "Display",
            "Windows",
            null,
            null,
            false,
            "Windows",
            null,
            null,
            null,
            IsHidden: isHidden,
            IsOptional: isOptional,
            IsAdvancedCandidate: isAdvanced,
            SearchSource: "Windows Update online");

    [Fact]
    public void HomeCountsMatchDisjointWuaFiltersAndNeverOverstateInstallable()
    {
        var recommendedGpu = CreateUpdate("NVIDIA GeForce RTX 4060");
        var optionalKiller = CreateUpdate("Intel Killer Wi-Fi",
            isOptional: true);
        var optionalHidden = CreateUpdate("Realtek hidden optional",
            isHidden: true, isOptional: true);
        var advanced = CreateUpdate("Realtek advanced",
            isAdvanced: true);
        var optionalAdvanced = CreateUpdate("Intel advanced optional",
            isOptional: true, isAdvanced: true);
        var bios = CreateUpdate("NVIDIA BIOS update");
        var unknownSource = CreateUpdate("Unknown OEM website") with
        {
            SearchSource = "Unverified OEM website"
        };
        var badGuid = CreateUpdate("Bad GUID WUA title") with
        {
            Id = "PRIVATE_SERIAL_NOT_A_GUID"
        };
        var unknownOptional = CreateUpdate("Fake website BrowseOnly") with
        {
            SearchSource = "Unverified OEM website",
            IsOptional = true
        };
        var updates = new[]
        {
            recommendedGpu, optionalKiller, optionalHidden, advanced,
            optionalAdvanced, bios, unknownSource, badGuid, unknownOptional
        };

        var summary = DriverSearchSummary.Create(updates, 2);
        Assert.Equal(1, summary.Recommended);
        Assert.Equal(1, summary.Optional);
        Assert.Equal(7, summary.Advanced);
        Assert.Equal(2, summary.OemSources);
        Assert.Equal(updates.Length,
            summary.Recommended + summary.Optional + summary.Advanced);

        Assert.Equal(summary.Recommended, updates.Count(u =>
            CompactResultFilter.IsVisible(u, "recommended")));
        Assert.Equal(summary.Recommended, updates.Count(u =>
            CompactResultFilter.IsVisible(u, "normal")));
        Assert.Equal(summary.Optional, updates.Count(u =>
            CompactResultFilter.IsVisible(u, "optional")));
        Assert.Equal(summary.Advanced, updates.Count(u =>
            CompactResultFilter.IsVisible(u, "advanced")));
        Assert.Equal(updates.Length, updates.Count(u =>
            CompactResultFilter.IsVisible(u, "all")));
        Assert.Equal([recommendedGpu.Id],
            QuickUpdatePolicy.AllOrdinaryIds(updates));
        Assert.False(CompactResultFilter.IsVisible(bios, "recommended"));
        Assert.False(CompactResultFilter.IsVisible(unknownSource, "recommended"));
        Assert.False(CompactResultFilter.IsVisible(badGuid, "recommended"));
        Assert.False(CompactResultFilter.IsVisible(optionalKiller, "advanced"));
        Assert.True(CompactResultFilter.IsVisible(optionalHidden, "advanced"));
    }

    [Fact]
    public void NoCandidatesDoesNotInventRecommendationsOrOptionalOffers()
    {
        var summary = DriverSearchSummary.Create([], -5);
        Assert.Equal(0, summary.Recommended);
        Assert.Equal(0, summary.Optional);
        Assert.Equal(0, summary.Advanced);
        Assert.Equal(0, summary.OemSources);
    }

}
