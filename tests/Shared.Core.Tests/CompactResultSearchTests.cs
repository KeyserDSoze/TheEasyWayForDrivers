using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class CompactResultSearchTests
{
    private static DriverUpdateInfo Candidate() => new(
        Guid.NewGuid().ToString("D"), "NVIDIA display controller",
        "Sensitive internal description with private-token",
        "Display", "NVIDIA", "32.0.1", null, false,
        "NVIDIA Corporation", "RTX 4060 Laptop",
        @"PCI\VEN_10DE&DEV_28A0", null,
        SearchSource: "Microsoft Update online");

    [Theory]
    [InlineData("nvidia")]
    [InlineData("  rTx 4060  ")]
    [InlineData("display")]
    [InlineData("microsoft update")]
    [InlineData("Corporation")]
    public void SearchesVisibleDriverMetadataIgnoringCase(string query)
    {
        Assert.True(CompactResultSearch.Matches(Candidate(), query));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankSearchDoesNotHideCandidates(string? query)
    {
        Assert.True(CompactResultSearch.Matches(Candidate(), query));
    }

    [Theory]
    [InlineData("AMD")]
    [InlineData("PCI\\VEN_10DE")]
    [InlineData("private-token")]
    public void NeverMatchesPrivateIdsOrDescription(string query)
    {
        Assert.False(CompactResultSearch.Matches(Candidate(), query));
    }

    [Fact]
    public void HandlesMissingOptionalMetadata()
    {
        var basic = new DriverUpdateInfo(
            Guid.NewGuid().ToString("D"), "Intel Wi-Fi",
            null, null, null, null, null, false, null, null, null, null);
        Assert.True(CompactResultSearch.Matches(basic, "wifi") == false);
        Assert.True(CompactResultSearch.Matches(basic, "wi-fi"));
        Assert.False(CompactResultSearch.Matches(basic, "Realtek"));
    }
}
