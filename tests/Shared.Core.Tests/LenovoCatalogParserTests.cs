using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class LenovoCatalogParserTests
{
    [Fact]
    public void ParsesDocumentedLenovoDriverFieldsOnly()
    {
        var json = """
            [{"ID":"n3jw605w","Name":"Audio driver","Category":"Audio",
              "Version":"4.1","PackageType":"2","ReleaseDate":"2026-02-14"},
             {"ID":"bios","Name":"BIOS update","PackageType":"3"},
             {"ID":"firmware","Name":"Firmware","PackageType":"4"}]
            """;
        var result = Assert.Single(LenovoCatalogParser.Parse(json));
        Assert.Equal("n3jw605w", result.Id);
        Assert.Equal("Audio driver", result.Name);
    }

    [Fact]
    public void SingleObjectAndNumericPackageTypeAreSupported()
    {
        var json = """{"ID":"drv1","Name":"Chipset","Version":"2","PackageType":2}""";
        Assert.Equal("drv1", Assert.Single(LenovoCatalogParser.Parse(json)).Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"ID":"x","Name":"No type"}""")]
    [InlineData("""{"ID":"x","PackageType":"2"}""")]
    [InlineData("""{"Name":"Missing ID","PackageType":"2"}""")]
    public void EmptyOrIncompleteMetadataIsNotClaimedAsDriver(string json) =>
        Assert.Empty(LenovoCatalogParser.Parse(json));

    [Theory]
    [InlineData("true")]
    [InlineData("4")]
    [InlineData("not JSON")]
    public void RejectsUnexpectedJson(string json) =>
        Assert.ThrowsAny<Exception>(() => LenovoCatalogParser.Parse(json));

    [Fact]
    public void RejectsOversizedPayload() =>
        Assert.Throws<InvalidDataException>(() =>
            LenovoCatalogParser.Parse(new string('x', 1024 * 1024 + 1)));
    [Fact]
    public void LenovoCatalogFindsDriversBeyondFiftyBiosAndSoftwareRows()
    {
        var prefix = Enumerable.Range(0, 75)
            .Select(i => $$"""{"ID":"bios{{i}}","Name":"BIOS","PackageType":"3"}""");
        var tail = new[]
        {
            """{"ID":"nvidia","Name":"NVIDIA","PackageType":"2","Category":"Graphics"}""",
            """{"ID":"killer","Name":"Killer","PackageType":"2","Category":"Network"}"""
        };
        var rows = LenovoCatalogParser.Parse(
            "[" + string.Join(",", prefix.Concat(tail)) + "]");
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Id == "nvidia");
        Assert.Contains(rows, row => row.Id == "killer");
    }

    [Fact]
    public void LenovoCatalogFairSelectionAndDeduplicationAreBounded()
    {
        var audio = Enumerable.Range(0, 110)
            .Select(i => $$"""{"ID":"audio{{i}}","Name":"Audio","PackageType":"2","Category":"Audio"}""");
        var tail = new[]
        {
            """{"ID":"NVIDIA","Name":"Graphics","PackageType":"2","Category":"Graphics"}""",
            """{"ID":"killer","Name":"Network","PackageType":"2","Category":"Network"}""",
            """{"ID":"nvidia","Name":"Duplicate","PackageType":"2","Category":"Graphics"}"""
        };
        var rows = LenovoCatalogParser.Parse(
            "[" + string.Join(",", audio.Concat(tail)) + "]");
        Assert.Equal(LenovoCatalogParser.MaximumDriverResults, rows.Count);
        Assert.Contains(rows, row => row.Id == "NVIDIA" && row.Name == "Graphics");
        Assert.Contains(rows, row => row.Id == "killer");
        Assert.Equal(rows.Count, rows.Select(row => row.Id).Distinct(
            StringComparer.OrdinalIgnoreCase).Count());
    }

}
