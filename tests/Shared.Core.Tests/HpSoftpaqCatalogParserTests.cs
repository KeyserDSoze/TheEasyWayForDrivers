using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class HpSoftpaqCatalogParserTests
{
    [Fact]
    public void Parse_AcceptsHpDocumentedFields_OnlyDrivers()
    {
        var json = """
            [{"Id":"sp12345","Name":"Audio Driver","Category":"Driver",
              "Version":"1.2","ReleaseType":"Recommended","ReleaseDate":"2026-01-01"},
             {"Id":"sp99999","Name":"UEFI BIOS","Category":"Bios","Version":"2"}]
            """;
        var entry = Assert.Single(HpSoftpaqCatalogParser.Parse(json));
        Assert.Equal("sp12345", entry.Id);
        Assert.Equal("Audio Driver", entry.Title);
    }

    [Fact]
    public void Parse_IncludesModernHpDriverSubcategoriesWithoutFirmware()
    {
        const string json = """
            [
             {"Id":"sp-gpu","Name":"Intel graphics","Category":"Driver - Graphics","Version":"32.0"},
             {"Id":"sp-net","Name":"WiFi","Category":"Driver - Network","Version":"23.4"},
             {"Id":"sp-audio","Name":"Audio","Category":"Driver - Audio"},
             {"Id":"sp-driverpack","Name":"Deployment driver pack","Category":"Driverpack"},
             {"Id":"sp-firmware","Name":"USB firmware","Category":"Firmware"},
             {"Id":"sp-bios","Name":"BIOS","Category":"Bios"},
             {"Id":"sp-soft","Name":"Utility","Category":"Software"}
            ]
            """;

        var results = HpSoftpaqCatalogParser.Parse(json);
        Assert.Equal(3, results.Count);
        Assert.Contains(results, x => x.Id == "sp-gpu" && x.Version == "32.0");
        Assert.Contains(results, x => x.Id == "sp-net");
        Assert.Contains(results, x => x.Id == "sp-audio");
    }

    [Theory]
    [InlineData("Driver", true)]
    [InlineData("driver - graphics", true)]
    [InlineData("Driver - Keyboard, Mouse and Input Devices", true)]
    [InlineData("Driver - Network", true)]
    [InlineData("Driverpack", false)]
    [InlineData("Drivers", false)]
    [InlineData("Driver - ", false)]
    [InlineData("Driver -", false)]
    [InlineData("Driver -Firmware", false)]
    [InlineData("Driver - Networkx", true)]
    [InlineData("Firmware", false)]
    [InlineData("", false)]
    public void CategoryPolicy_HonorsTheDriverPrefixBoundary(
        string category, bool expected) =>
        Assert.Equal(expected, HpSoftpaqCatalogParser.IsDriverCategory(category));

    [Fact]
    public void Parse_RejectsRecordsWithoutRealIdentifiers()
    {
        var json = """
            [{"Name":"No ID","Category":"Driver"},
             {"Id":"sp123","Category":"Driver"},
             {"Id":"sp456","Name":"Valid","Category":"Driver"}]
            """;
        var result = Assert.Single(HpSoftpaqCatalogParser.Parse(json));
        Assert.Equal("sp456", result.Id);
    }

    [Fact]
    public void Parse_AcceptsOneObject() =>
        Assert.Single(HpSoftpaqCatalogParser.Parse(
            """{"Id":"sp11","Name":"Test","Category":"Driver"}"""));

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Parse_HandlesNoResults(string json) =>
        Assert.Empty(HpSoftpaqCatalogParser.Parse(json));

    [Theory]
    [InlineData("42")]
    [InlineData("false")]
    [InlineData("not json")]
    public void Parse_RejectsUnexpectedPayload(string json) =>
        Assert.ThrowsAny<Exception>(() => HpSoftpaqCatalogParser.Parse(json));
}
