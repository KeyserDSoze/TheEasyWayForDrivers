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
    [InlineData(" driver ", true)]
    [InlineData("driver - graphics", true)]
    [InlineData("Driver - Chipset", true)]
    [InlineData("Driver - Audio", true)]
    [InlineData("Driver - Keyboard, Mouse and Input Devices", true)]
    [InlineData("Driver - Enabling", true)]
    [InlineData("Driver - Network", true)]
    [InlineData("Driver - Storage", true)]
    [InlineData("Driver - Controller", true)]
    [InlineData("Driverpack", false)]
    [InlineData("Drivers", false)]
    [InlineData("Driver - ", false)]
    [InlineData("Driver -", false)]
    [InlineData("Driver -Firmware", false)]
    [InlineData("Driver - Firmware", false)]
    [InlineData("Driver - BIOS", false)]
    [InlineData("Driver - Software", false)]
    [InlineData("Driver - Diagnostics", false)]
    [InlineData("Driver - Networkx", false)]
    [InlineData("Driver -  Network", false)]
    [InlineData("Driver - Network\t", false)]
    [InlineData("Driver\r\n", false)]
    [InlineData("Driver\u00A0", false)]
    [InlineData("Firmware", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CategoryPolicy_OnlyAcceptsCmslReaderAllowlist(
        string? category, bool expected) =>
        Assert.Equal(expected, HpSoftpaqCatalogParser.IsDriverCategory(category));

    [Fact]
    public void Parse_UnknownDriverPrefixedCategoriesAreNotDriverMetadata()
    {
        const string json = """
            [
              {"Id":"sp-firmware","Name":"UEFI Firmware","Category":"Driver - Firmware"},
              {"Id":"sp-bios","Name":"BIOS","Category":"Driver - BIOS"},
              {"Id":"sp-nearmiss","Name":"Fake WiFi","Category":"Driver - Networkx"},
              {"Id":"sp-unknown","Name":"Utility","Category":"Driver - Diagnostics"},
              {"Id":"sp-control","Name":"Tab name","Category":"Driver - Network\t"},
              {"Id":"sp-type","Name":"Invalid field type","Category":42},
              {"Id":"sp-gpu","Name":"NVIDIA GPU","Category":"Driver - Graphics"},
              {"Id":"sp-wifi","Name":"Killer WiFi","Category":"Driver - Network"},
              {"Id":"sp-audio","Name":"Audio","Category":"Driver - Audio"}
            ]
            """;

        var rows = HpSoftpaqCatalogParser.Parse(json);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "sp-gpu", "sp-wifi", "sp-audio" },
            rows.Select(row => row.Id));
    }

    [Fact]
    public void MalformedCategoryCannotPreemptValidSameSoftpaqId()
    {
        const string json = """
            [
              {"Id":"sp12","Name":"Bogus firmware","Category":"Driver - Firmware"},
              {"Id":"sp12","Name":"Valid graphics","Category":"Driver - Graphics"},
              {"Id":"sp13","Name":"Tab field","Category":"Driver\t"},
              {"Id":"sp14","Name":"Good audio","Category":"Driver - Audio"}
            ]
            """;
        var rows = HpSoftpaqCatalogParser.Parse(json);
        Assert.Equal(2, rows.Count);
        Assert.Equal("sp12", rows[0].Id);
        Assert.Equal("Valid graphics", rows[0].Title);
        Assert.Equal("sp14", rows[1].Id);
    }

    [Fact]
    public void UnknownCategoriesCannotConsumeTheHundredTwentyDriverSlots()
    {
        var bogus = Enumerable.Range(0, 170)
            .Select(i => $"""{"Id":"unknown{{i}}","Name":"Firmware {{i}}","Category":"Driver - Firmware"}""");
        var keyboard = Enumerable.Range(0, 130)
            .Select(i => $"""{"Id":"sp{{i}}","Name":"Keyboard {{i}}","Category":"Driver - Keyboard, Mouse and Input Devices"}""");
        var rare = new[]
        {
            """{"Id":"sp-gpu","Name":"NVIDIA","Category":"Driver - Graphics"}""",
            """{"Id":"sp-net","Name":"Killer","Category":"Driver - Network"}""",
            """{"Id":"sp-audio","Name":"Realtek","Category":"Driver - Audio"}"""
        };
        var rows = HpSoftpaqCatalogParser.Parse(
            "[" + string.Join(",", bogus.Concat(keyboard).Concat(rare)) + "]");
        Assert.Equal(HpSoftpaqCatalogParser.MaximumDriverResults, rows.Count);
        Assert.Contains(rows, row => row.Id == "sp-gpu");
        Assert.Contains(rows, row => row.Id == "sp-net");
        Assert.Contains(rows, row => row.Id == "sp-audio");
        Assert.DoesNotContain(rows, row => row.Id.StartsWith("unknown"));
    }

    [Fact]
    public void DuplicateSoftPaqsAcrossCategoriesAreShownOnlyOnce()
    {
        const string json = """
            [
              {"Id":"sp300","Name":"Driver","Category":"Driver"},
              {"Id":"SP300","Name":"Same driver","Category":"Driver - Graphics"},
              {"Id":"sp301","Name":"Network","Category":"Driver - Network"}
            ]
            """;
        var result = HpSoftpaqCatalogParser.Parse(json);
        Assert.Equal(2, result.Count);
        Assert.Equal("sp300", result[0].Id);
        Assert.Equal("sp301", result[1].Id);
    }

    [Fact]
    public void AcceptsMoreThanFiftyUniqueDriverSoftpaqs()
    {
        var entries = Enumerable.Range(0, 75).Select(i =>
            "{\"Id\":\"sp" + i + "\",\"Name\":\"Device " + i +
            "\",\"Category\":\"Driver - Audio\"}");
        Assert.Equal(75, HpSoftpaqCatalogParser.Parse(
            "[" + string.Join(",", entries) + "]").Count);
    }

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
    [Fact]
    public void DriversAfterFirst120NonDriverRowsAreNotLost()
    {
        var ignored = Enumerable.Range(0, 170)
            .Select(i => $$"""{"Id":"firm{{i}}","Name":"Firmware {{i}}","Category":"Firmware"}""");
        var drivers = new[]
        {
            """{"Id":"sp-gpu","Name":"NVIDIA GPU","Category":"Driver - Graphics"}""",
            """{"Id":"sp-network","Name":"Intel Killer WiFi","Category":"Driver - Network"}""",
            """{"Id":"sp-audio","Name":"Realtek Audio","Category":"Driver - Audio"}"""
        };
        var found = HpSoftpaqCatalogParser.Parse(
            "[" + string.Join(",", ignored.Concat(drivers)) + "]");
        Assert.Equal(3, found.Count);
        Assert.Contains(found, row => row.Id == "sp-gpu");
        Assert.Contains(found, row => row.Id == "sp-network");
        Assert.Contains(found, row => row.Id == "sp-audio");
    }

    [Fact]
    public void MoreThan120DriverRowsKeepRareGraphicsNetworkAndAudioCategories()
    {
        var many = Enumerable.Range(0, 170)
            .Select(i => $$"""{"Id":"sp{{i}}","Name":"Generic keyboard {{i}}","Category":"Driver - Keyboard, Mouse and Input Devices"}""");
        var rare = new[]
        {
            """{"Id":"sp-gpu","Name":"NVIDIA","Category":"Driver - Graphics"}""",
            """{"Id":"sp-killer","Name":"Killer Wi-Fi","Category":"Driver - Network"}""",
            """{"Id":"sp-realtek","Name":"Realtek","Category":"Driver - Audio"}"""
        };
        var found = HpSoftpaqCatalogParser.Parse(
            "[" + string.Join(",", many.Concat(rare)) + "]");
        Assert.Equal(HpSoftpaqCatalogParser.MaximumDriverResults, found.Count);
        Assert.Contains(found, row => row.Id == "sp-gpu");
        Assert.Contains(found, row => row.Id == "sp-killer");
        Assert.Contains(found, row => row.Id == "sp-realtek");
        Assert.DoesNotContain(found, row => row.Id.StartsWith("sp-bios"));
    }

}
