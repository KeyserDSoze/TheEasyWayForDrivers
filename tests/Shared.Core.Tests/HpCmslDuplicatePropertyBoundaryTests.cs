using System.Text.Json;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #508: contradictory JSON properties from CMSL must never fabricate a
/// published HP driver, consume a valid SoftPaq identity or leak private
/// metadata. Catalog entries are read-only, not per-device applicability.
/// </summary>
public sealed class HpCmslDuplicatePropertyBoundaryTests
{
    private static string Valid(string id = "sp-gpu",
        string category = "Driver - Graphics", string name = "NVIDIA GPU") =>
        JsonSerializer.Serialize(new
        {
            Id = id, Name = name, Category = category, Version = "32.0.0.1"
        });

    [Theory]
    [InlineData("\"Category\":\"Firmware\",\"Category\":\"Driver - Graphics\"")]
    [InlineData("\"Category\":\"Driver - Graphics\",\"Category\":\"Firmware\"")]
    [InlineData("\"Category\":\"Driver - Network\",\"CATEGORY\":\"Driver - Network\"")]
    [InlineData("\"Category\":\"Driver - Audio\",\"category\":\"Firmware\"")]
    public void DuplicateCategoryNeverPromotesAnAmbiguousRecord(
        string categoryFields)
    {
        var json = "[{\"Id\":\"sp-gpu\",\"Name\":\"GPU\"," +
            categoryFields + "}," + Valid("sp-killer", "Driver - Network",
                "Intel Killer") + "]";

        var item = Assert.Single(HpSoftpaqCatalogParser.Parse(json));
        Assert.Equal("sp-killer", item.Id);
        Assert.Equal("Intel Killer", item.Title);
    }

    [Theory]
    [InlineData("\"Id\":\"sp-firmware\",\"Id\":\"sp-gpu\"")]
    [InlineData("\"Id\":\"sp-gpu\",\"ID\":\"sp-gpu\"")]
    [InlineData("\"Id\":\"sp-gpu\",\"id\":\"sp-firmware\"")]
    public void DuplicateIdentityCannotPreemptLaterValidSoftPaq(
        string idFields)
    {
        var json = "[{" + idFields +
            ",\"Name\":\"Suspicious\",\"Category\":\"Driver - Graphics\"}," +
            Valid() + "]";
        var row = Assert.Single(HpSoftpaqCatalogParser.Parse(json));
        Assert.Equal("sp-gpu", row.Id);
        Assert.Equal("NVIDIA GPU", row.Title);
    }

    [Theory]
    [InlineData("\"Name\":\"Firmware\",\"Name\":\"NVIDIA GPU\"")]
    [InlineData("\"Name\":\"GPU\",\"NAME\":\"GPU\"")]
    [InlineData("\"Version\":\"99.0\",\"Version\":\"1.0\"")]
    [InlineData("\"ReleaseType\":\"Critical\",\"ReleaseType\":\"Recommended\"")]
    [InlineData("\"ReleaseDate\":\"2026-11-16\",\"ReleaseDate\":\"2025-01-01\"")]
    public void DuplicateDisplayMetadataIsNotAcceptedAsPublishedEvidence(
        string metadataFields)
    {
        var json = "[{\"Id\":\"sp-gpu\",\"Category\":\"Driver - Graphics\"," +
            metadataFields + "}," + Valid("sp-audio", "Driver - Audio",
                "Realtek Audio") + "]";
        var row = Assert.Single(HpSoftpaqCatalogParser.Parse(json));
        Assert.Equal("sp-audio", row.Id);
    }

    [Fact]
    public void RepeatedUnknownPropertyDoesNotBreakOfficialDriverRows()
    {
        const string json = """
            [{"Id":"sp-killer","Name":"Intel Killer",
              "Category":"Driver - Network","Unused":1,"Unused":2},
             {"Id":"sp-audio","Name":"Realtek Audio",
              "Category":"Driver - Audio"}]
            """;
        var entries = HpSoftpaqCatalogParser.Parse(json);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, item => item.Id == "sp-killer");
        Assert.Contains(entries, item => item.Id == "sp-audio");
    }

    [Fact]
    public void ConflictingFirmwareRowCannotHideIndependentRareCategories()
    {
        var ordinary = Enumerable.Range(0, 160).Select(index =>
            Valid("sp-keys-" + index, "Driver - Keyboard, Mouse and Input Devices",
                "Generic keyboard"));
        const string bad = """
            {"Id":"sp-gpu","Name":"Fake graphics",
             "Category":"Firmware","Category":"Driver - Graphics"}
            """;
        var valid = new[]
        {
            Valid("sp-gpu", "Driver - Graphics", "NVIDIA GPU"),
            Valid("sp-killer", "Driver - Network", "Intel Killer"),
            Valid("sp-realtek", "Driver - Audio", "Realtek")
        };
        var result = HpSoftpaqCatalogParser.Parse(
            "[" + string.Join(",", ordinary.Concat([bad]).Concat(valid)) + "]");

        Assert.Equal(HpSoftpaqCatalogParser.MaximumDriverResults, result.Count);
        Assert.Contains(result, row =>
            row.Id == "sp-gpu" && row.Title == "NVIDIA GPU");
        Assert.Contains(result, row => row.Id == "sp-killer");
        Assert.Contains(result, row => row.Id == "sp-realtek");
        Assert.DoesNotContain(result, row => row.Title == "Fake graphics");
    }

    [Fact]
    public void DuplicationCannotBypassTheRawThousandRowLimit()
    {
        const string bad = """
            {"Id":"sp0","Name":"Private",
             "Category":"Firmware","Category":"Driver - Graphics"}
            """;
        var rows = Enumerable.Repeat(bad,
            HpSoftpaqCatalogParser.MaximumReceivedRows + 1);
        Assert.Throws<InvalidDataException>(() =>
            HpSoftpaqCatalogParser.Parse("[" + string.Join(",", rows) + "]"));
    }

    [Fact]
    public void DuplicateFieldsNeverExposePrivateTokensInSurvivingMetadata()
    {
        const string malicious = """
            {"Id":"sp-private","Id":"sp-gpu",
             "Name":"C:\\Users\\PRIVATE_DEVICE\\serial.txt",
             "Category":"Driver - Graphics"}
            """;
        var items = HpSoftpaqCatalogParser.Parse(
            "[" + malicious + "," + Valid() + "]");
        var row = Assert.Single(items);
        Assert.Equal("sp-gpu", row.Id);
        Assert.Equal("NVIDIA GPU", row.Title);
        Assert.DoesNotContain("PRIVATE_DEVICE", row.Title + row.Id);
    }
}
