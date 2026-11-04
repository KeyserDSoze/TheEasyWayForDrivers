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
        Assert.False(CompactResultFilter.IsVisible(optional, "recommended"));
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
}
