using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class LenovoApplicableUpdateParserTests
{
    [Fact]
    public void ReturnsNeededDriversOnly()
    {
        var json = """
            [
             {"PackageID":"drv","Title":"NVIDIA display","Type":"Driver","Category":"Graphics","IsApplicable":true,"IsInstalled":false,"Version":"32.1","Severity":"Recommended"},
             {"PackageID":"bios","Title":"BIOS","Type":"BIOS","Category":"Driver","IsApplicable":true,"IsInstalled":false},
             {"PackageID":"firmware","Title":"Firmware","Type":4,"IsApplicable":true,"IsInstalled":false},
             {"PackageID":"installed","Title":"Installed","Type":2,"IsApplicable":true,"IsInstalled":true},
             {"PackageID":"other-pc","Title":"Not applicable","Type":"Driver","IsApplicable":false,"IsInstalled":false},
             {"PackageID":"missing","Title":"No evidence","Type":"Driver"},
             {"PackageID":"drv","Title":"Duplicate","Type":"Driver","IsApplicable":true,"IsInstalled":false}
            ]
            """;
        var row = Assert.Single(LenovoApplicableUpdateParser.Parse(json));
        Assert.Equal("drv", row.PackageId);
        Assert.Equal("32.1", row.Version);
    }

    [Theory]
    [InlineData("""{"PackageID":"a","Title":"Net","Type":2,"IsApplicable":true,"IsInstalled":false}""")]
    [InlineData("""{"PackageID":"a","Title":"Net","Category":"Driver","IsApplicable":true,"IsInstalled":false}""")]
    public void SupportsDocumentedTypeVariants(string json) =>
        Assert.Equal("a", Assert.Single(
            LenovoApplicableUpdateParser.Parse(json)).PackageId);

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"PackageID":"x","Title":"Bad","Type":"Driver"}""")]
    [InlineData("""{"PackageID":"x","Title":"Bad","Type":"Driver","IsApplicable":"true","IsInstalled":false}""")]
    [InlineData("""{"PackageID":"x","Title":"Bad","Type":3,"IsApplicable":true,"IsInstalled":false}""")]
    [InlineData("""{"Title":"No ID","Type":"Driver","IsApplicable":true,"IsInstalled":false}""")]
    public void RejectsUnverifiedDriverClaims(string json) =>
        Assert.Empty(LenovoApplicableUpdateParser.Parse(json));

    [Fact]
    public void RejectsOversizedJson() =>
        Assert.Throws<InvalidDataException>(() =>
            LenovoApplicableUpdateParser.Parse(new string('x', 1024 * 1024 + 1)));

    [Fact]
    public void VendorResultStaysOutsideInstallableWindowsUpdate()
    {
        var package = new LenovoApplicableUpdateParser.Package(
            "id", "Graphics driver", "32.1", "2026-10-02", "Critical");
        var row = Assert.Single(LenovoApplicableUpdateParser.ToVendorChecks([package]));
        Assert.True(row.IsCatalogMetadata);
        Assert.True(row.IsVendorApplicable);
        Assert.Equal(0, row.WuaMatches);
        Assert.Contains("necessario", row.StatusText);
        Assert.Contains("non installabile", row.StatusText);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(row.OfficialUrl));
    }

    [Fact]
    public void CapsResultsAndFiltersDuplicateIds()
    {
        var items = Enumerable.Range(0, 95).Select(i =>
            "{\"PackageID\":\"" + i + "\",\"Title\":\"Driver\",\"Type\":2," +
            "\"IsApplicable\":true,\"IsInstalled\":false}");
        Assert.Equal(80, LenovoApplicableUpdateParser.Parse(
            "[" + string.Join(",", items) + "]").Count);
    }
    [Fact]
    public void NeededGraphicsAndNetworkAfter80UnrelatedUpdatesAreFound()
    {
        var prefix = Enumerable.Range(0, 130)
            .Select(i => $$"""{"PackageID":"bios{{i}}","Title":"BIOS","Type":"BIOS","IsApplicable":true,"IsInstalled":false}""");
        var suffix = new[]
        {
            """{"PackageID":"nvidia","Title":"NVIDIA","Type":"Driver","Category":"Graphics","IsApplicable":true,"IsInstalled":false}""",
            """{"PackageID":"killer","Title":"Killer","Type":"Driver","Category":"Network","IsApplicable":true,"IsInstalled":false}""",
            """{"PackageID":"installed","Title":"Installed","Type":"Driver","IsApplicable":true,"IsInstalled":true}"""
        };
        var drivers = LenovoApplicableUpdateParser.Parse(
            "[" + string.Join(",", prefix.Concat(suffix)) + "]");
        Assert.Equal(2, drivers.Count);
        Assert.Contains(drivers, row => row.PackageId == "nvidia");
        Assert.Contains(drivers, row => row.PackageId == "killer");
    }

    [Fact]
    public void Over80ApplicableDriversDoNotHideRareCategories()
    {
        var first = Enumerable.Range(0, 155)
            .Select(i => $$"""{"PackageID":"audio{{i}}","Title":"Audio","Type":"Driver","Category":"Audio","IsApplicable":true,"IsInstalled":false}""");
        var tail = new[]
        {
            """{"PackageID":"nvidia","Title":"NVIDIA","Type":"Driver","Category":"Graphics","IsApplicable":true,"IsInstalled":false}""",
            """{"PackageID":"killer","Title":"Killer","Type":"Driver","Category":"Network","IsApplicable":true,"IsInstalled":false}"""
        };
        var result = LenovoApplicableUpdateParser.Parse(
            "[" + string.Join(",", first.Concat(tail)) + "]");
        Assert.Equal(LenovoApplicableUpdateParser.MaximumDriverResults, result.Count);
        Assert.Contains(result, row => row.PackageId == "nvidia");
        Assert.Contains(result, row => row.PackageId == "killer");
        Assert.All(result, row => Assert.NotEqual("Installed", row.Title));
    }

}
