using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class CompactResultFilterTests
{
    private static DriverUpdateInfo Candidate(
        string source = "Microsoft Update online",
        bool advanced = false,
        bool hidden = false,
        bool optional = false) =>
        new(Guid.NewGuid().ToString("D"), "NVIDIA GPU Driver",
            null, "Display", "NVIDIA", null, null, false,
            "NVIDIA", "RTX 4060 Laptop", @"PCI\VEN_10DE&DEV_28A0",
            null, hidden, optional, advanced, source);

    [Fact]
    public void DefaultAndAllShowRegularMuAndAdvancedResults()
    {
        var muNormal = Candidate();
        var muAdvanced = Candidate(advanced: true,
            source: "Microsoft Update online · avanzato");
        var wua = Candidate(source: "Windows Update online");
        var arbitrary = Candidate(source: "Unexpected OEM", hidden: true);

        foreach (var item in new[] { muNormal, muAdvanced, wua, arbitrary })
        {
            Assert.True(CompactResultFilter.IsVisible(item, null));
            Assert.True(CompactResultFilter.IsVisible(item, "all"));
        }
    }

    [Fact]
    public void EligibleMicrosoftUpdateAppearsInQuickInstallFilter()
    {
        var mu = Candidate();
        Assert.True(QuickUpdatePolicy.IsEligible(mu));
        Assert.True(CompactResultFilter.IsVisible(mu, "normal"));
        Assert.True(CompactResultFilter.IsVisible(mu, "recommended"));
        Assert.False(CompactResultFilter.IsVisible(mu, "advanced"));
    }

    [Fact]
    public void AdvancedMicrosoftUpdateCandidateRemainsVisibleButNotQuickInstallable()
    {
        var advanced = Candidate(advanced: true,
            source: "Microsoft Update online · avanzato");
        Assert.True(CompactResultFilter.IsVisible(advanced, null));
        Assert.True(CompactResultFilter.IsVisible(advanced, "advanced"));
        Assert.False(CompactResultFilter.IsVisible(advanced, "normal"));
        Assert.False(QuickUpdatePolicy.IsEligible(advanced));
    }

    [Fact]
    public void OptionalAndHiddenHaveCorrectFilters()
    {
        var optional = Candidate(optional: true);
        var hidden = Candidate(hidden: true);
        Assert.True(CompactResultFilter.IsVisible(optional, "optional"));
        Assert.False(CompactResultFilter.IsVisible(optional, "advanced"));
        Assert.False(CompactResultFilter.IsVisible(optional, "normal"));
        Assert.False(CompactResultFilter.IsVisible(optional, "recommended"));
        Assert.False(QuickUpdatePolicy.IsEligible(optional));
        Assert.True(CompactResultFilter.IsVisible(hidden, "advanced"));
        Assert.False(CompactResultFilter.IsVisible(hidden, "normal"));
    }

    [Fact]
    public void SourceNameFromArbitraryOemCannotBePresentedAsQuickInstallable()
    {
        var unknown = Candidate(source: "NVIDIA website");
        Assert.True(CompactResultFilter.IsVisible(unknown, "all"));
        Assert.False(CompactResultFilter.IsVisible(unknown, "normal"));
        Assert.False(QuickUpdatePolicy.IsEligible(unknown));
    }

    [Fact]
    public void ReviewCategoriesAreExclusiveAndMatchActualEligibility()
    {
        var normal = Candidate();
        var optional = Candidate(optional: true);
        var hiddenOptional = Candidate(optional: true, hidden: true);
        var advancedOptional = Candidate(optional: true, advanced: true,
            source: "Windows Update online · avanzato");
        var fakeOptional = Candidate(optional: true, source: "Unknown OEM");
        var firmware = Candidate() with { Title = "BIOS Firmware Update" };
        var invalidGuid = Candidate() with { Id = "PRIVATE_INSTANCE" };
        var updates = new[]
        {
            normal, optional, hiddenOptional, advancedOptional, fakeOptional,
            firmware, invalidGuid
        };
        foreach (var item in updates)
        {
            var visibleCategories = new[]
            {
                "recommended", "optional", "advanced"
            }.Count(filter => CompactResultFilter.IsVisible(item, filter));
            Assert.Equal(1, visibleCategories);
            Assert.Equal(QuickUpdatePolicy.IsEligible(item),
                CompactResultFilter.IsVisible(item, "recommended"));
            Assert.Equal(QuickUpdatePolicy.IsEligible(item),
                CompactResultFilter.IsVisible(item, "normal"));
            Assert.True(CompactResultFilter.IsVisible(item, "all"));
        }
        Assert.True(CompactResultFilter.IsVisible(optional, "optional"));
        Assert.True(CompactResultFilter.IsVisible(hiddenOptional, "advanced"));
        Assert.True(CompactResultFilter.IsVisible(advancedOptional, "advanced"));
        Assert.True(CompactResultFilter.IsVisible(fakeOptional, "advanced"));
        Assert.True(CompactResultFilter.IsVisible(firmware, "advanced"));
        Assert.True(CompactResultFilter.IsVisible(invalidGuid, "advanced"));
    }

}
