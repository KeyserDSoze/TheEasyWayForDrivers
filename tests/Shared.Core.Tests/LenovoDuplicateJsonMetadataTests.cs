using System.Text.Json;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #509: conflicting Lenovo JSON metadata must never upgrade BIOS/firmware
/// or a non-applicable/installed package into OEM driver evidence.
/// Neither parser returns an installable Windows Update candidate.
/// </summary>
public sealed class LenovoDuplicateJsonMetadataTests
{
    private static string Needed(
        string id = "nvidia", string category = "Graphics",
        string title = "NVIDIA GPU") => JsonSerializer.Serialize(new
        {
            PackageID = id, Title = title, Type = "Driver", Category = category,
            IsApplicable = true, IsInstalled = false, Version = "32.1"
        });

    private static string Published(
        string id = "nvidia", string category = "Graphics",
        string title = "NVIDIA GPU") => JsonSerializer.Serialize(new
        {
            ID = id, Name = title, PackageType = 2, Category = category,
            Version = "32.1"
        });

    [Theory]
    [InlineData("""{"PackageID":"nvidia","Title":"Unverified","Type":"BIOS","Type":"Driver","IsApplicable":true,"IsInstalled":false}""")]
    [InlineData("""{"PackageID":"nvidia","Title":"Unverified","Type":"Driver","Type":"Driver","IsApplicable":true,"IsInstalled":false}""")]
    [InlineData("""{"PackageID":"nvidia","Title":"Unverified","Type":"Driver","TYPE":"Driver","IsApplicable":true,"IsInstalled":false}""")]
    [InlineData("""{"PackageID":"nvidia","Title":"Unverified","Type":"Driver","IsApplicable":false,"IsApplicable":true,"IsInstalled":false}""")]
    [InlineData("""{"PackageID":"nvidia","Title":"Unverified","Type":"Driver","IsApplicable":true,"ISAPPLICABLE":true,"IsInstalled":false}""")]
    [InlineData("""{"PackageID":"nvidia","Title":"Unverified","Type":"Driver","IsApplicable":true,"IsInstalled":true,"IsInstalled":false}""")]
    [InlineData("""{"PackageID":"nvidia","Title":"Unverified","Type":"Driver","IsApplicable":true,"IsInstalled":false,"isinstalled":false}""")]
    [InlineData("""{"PackageID":"nvidia","Title":"Unverified","Type":"Driver","Category":"Firmware","Category":"Graphics","IsApplicable":true,"IsInstalled":false}""")]
    public void NeededDriverDiscriminatorsCannotOverrideContradictoryKeys(
        string ambiguous)
    {
        var row = Assert.Single(LenovoApplicableUpdateParser.Parse(
            "[" + ambiguous + "," + Needed() + "]"));
        Assert.Equal("nvidia", row.PackageId);
        Assert.Equal("NVIDIA GPU", row.Title);
    }

    [Theory]
    [InlineData("\"PackageID\":\"old\",\"PackageID\":\"nvidia\"")]
    [InlineData("\"PackageID\":\"nvidia\",\"packageid\":\"nvidia\"")]
    [InlineData("\"Title\":\"Unverified\",\"Title\":\"NVIDIA GPU\"")]
    [InlineData("\"Version\":\"999.0\",\"Version\":\"32.1\"")]
    [InlineData("\"Severity\":\"Critical\",\"Severity\":\"Recommended\"")]
    [InlineData("\"ReleaseDate\":\"2025-01-01\",\"ReleaseDate\":\"2026-11-16\"")]
    public void NeededDuplicateIdentityOrDisplayDoesNotReserveAnId(
        string repeated)
    {
        const string verified =
            "\"Type\":\"Driver\",\"IsApplicable\":true,\"IsInstalled\":false";
        var json = "[{\"PackageID\":\"nvidia\",\"Title\":\"Unverified\"," +
            repeated + "," + verified + "}," + Needed() + "]";
        var row = Assert.Single(LenovoApplicableUpdateParser.Parse(json));
        Assert.Equal("nvidia", row.PackageId);
        Assert.Equal("NVIDIA GPU", row.Title);
    }

    [Fact]
    public void TypeAbsentWithDoubledCategoryCannotBecomeNeededDriver()
    {
        const string wrong = """
            {"PackageID":"nvidia","Title":"Firmware",
             "Category":"Firmware","Category":"Driver",
             "IsApplicable":true,"IsInstalled":false}
            """;
        var row = Assert.Single(LenovoApplicableUpdateParser.Parse(
            "[" + wrong + "," + Needed() + "]"));
        Assert.Equal("NVIDIA GPU", row.Title);
    }

    [Theory]
    [InlineData("\"PackageType\":4,\"PackageType\":2")]
    [InlineData("\"PackageType\":2,\"PackageType\":2")]
    [InlineData("\"PackageType\":\"2\",\"PACKAGETYPE\":\"2\"")]
    [InlineData("\"ID\":\"old\",\"ID\":\"nvidia\"")]
    [InlineData("\"ID\":\"nvidia\",\"id\":\"nvidia\"")]
    [InlineData("\"Name\":\"Untrusted\",\"Name\":\"NVIDIA GPU\"")]
    [InlineData("\"Version\":\"999.0\",\"Version\":\"32.1\"")]
    [InlineData("\"Category\":\"Firmware\",\"Category\":\"Graphics\"")]
    [InlineData("\"ReleaseDate\":\"2025-01-01\",\"ReleaseDate\":\"2026-11-16\"")]
    public void CatalogDuplicateMetadataCannotFabricatePublishedDriver(
        string repeated)
    {
        var bad = "{\"ID\":\"nvidia\",\"Name\":\"Untrusted\"," +
            "\"PackageType\":2,\"Category\":\"Graphics\"," + repeated + "}";
        var row = Assert.Single(LenovoCatalogParser.Parse(
            "[" + bad + "," + Published() + "]"));
        Assert.Equal("nvidia", row.Id);
        Assert.Equal("NVIDIA GPU", row.Name);
    }

    [Fact]
    public void UnknownRepeatedPropertiesLeaveIndependentDriverResults()
    {
        const string applicable = """
            {"PackageID":"nvidia","Title":"NVIDIA GPU","Type":"Driver",
             "Category":"Graphics","IsApplicable":true,"IsInstalled":false,
             "Unused":"alpha","Unused":"beta"}
            """;
        const string catalog = """
            {"ID":"nvidia","Name":"NVIDIA GPU","PackageType":2,
             "Category":"Graphics","Unused":1,"Unused":2}
            """;
        Assert.Equal("nvidia",
            Assert.Single(LenovoApplicableUpdateParser.Parse(applicable)).PackageId);
        Assert.Equal("nvidia",
            Assert.Single(LenovoCatalogParser.Parse(catalog)).Id);
    }

    [Fact]
    public void AmbiguousIdentityDoesNotSuppressRareGraphicsNetworkAudioRows()
    {
        var noise = Enumerable.Range(0, 145)
            .Select(i => Needed("audio" + i, "Audio", "Generic audio"));
        const string ambiguous = """
            {"PackageID":"nvidia","PackageID":"nvidia",
             "Title":"Fake graphics","Type":"Driver",
             "Category":"Graphics","IsApplicable":true,"IsInstalled":false}
            """;
        var needed = LenovoApplicableUpdateParser.Parse("[" +
            string.Join(",", noise.Concat([ambiguous]).Concat([
                Needed(), Needed("killer", "Network", "Intel Killer"),
                Needed("realtek", "Audio", "Realtek")
            ])) + "]");
        Assert.Equal(LenovoApplicableUpdateParser.MaximumDriverResults,
            needed.Count);
        Assert.Contains(needed, x =>
            x.PackageId == "nvidia" && x.Title == "NVIDIA GPU");
        Assert.Contains(needed, x => x.PackageId == "killer");
        Assert.Contains(needed, x => x.PackageId == "realtek");
        Assert.DoesNotContain(needed, x => x.Title == "Fake graphics");

        var many = Enumerable.Range(0, 90)
            .Select(i => Published("audio" + i, "Audio", "Generic audio"));
        const string badCatalog = """
            {"ID":"nvidia","ID":"nvidia","Name":"Fake",
             "PackageType":2,"Category":"Graphics"}
            """;
        var published = LenovoCatalogParser.Parse("[" +
            string.Join(",", many.Concat([badCatalog]).Concat([
                Published(), Published("killer", "Network", "Intel Killer"),
                Published("realtek", "Audio", "Realtek")
            ])) + "]");
        Assert.Equal(LenovoCatalogParser.MaximumDriverResults, published.Count);
        Assert.Contains(published, x => x.Id == "nvidia" && x.Name == "NVIDIA GPU");
        Assert.Contains(published, x => x.Id == "killer");
        Assert.Contains(published, x => x.Id == "realtek");
        Assert.DoesNotContain(published, x => x.Name == "Fake");
    }

    [Fact]
    public void RawResponseLimitStillPrecedesDuplicateMetadataFiltering()
    {
        const string wrongNeeded = """
            {"Type":"BIOS","Type":"Driver",
             "PackageID":"nvidia","Title":"Firmware",
             "IsApplicable":true,"IsInstalled":false}
            """;
        const string wrongCatalog = """
            {"ID":"nvidia","Name":"Firmware",
             "PackageType":4,"PackageType":2}
            """;
        Assert.Throws<InvalidDataException>(() =>
            LenovoApplicableUpdateParser.Parse("[" + string.Join(",",
                Enumerable.Repeat(wrongNeeded,
                    LenovoApplicableUpdateParser.MaximumReceivedRows + 1)) + "]"));
        Assert.Throws<InvalidDataException>(() =>
            LenovoCatalogParser.Parse("[" + string.Join(",",
                Enumerable.Repeat(wrongCatalog,
                    LenovoCatalogParser.MaximumReceivedRows + 1)) + "]"));
    }

    [Fact]
    public void PrivateDetailsOfAmbiguousRowsNeverReachCopiedVendorEvidence()
    {
        const string ambiguousNeeded = """
            {"PackageID":"nvidia","Title":"C:\\Users\\PRIVATE_USER\\driver.inf",
             "Type":"BIOS","Type":"Driver",
             "IsApplicable":true,"IsInstalled":false}
            """;
        const string ambiguousCatalog = """
            {"ID":"nvidia","Name":"https://private.example/PRIVATE_USER",
             "PackageType":4,"PackageType":2}
            """;
        var needed = LenovoApplicableUpdateParser.Parse("[" +
            ambiguousNeeded + "," + Needed() + "]");
        var published = LenovoCatalogParser.Parse("[" +
            ambiguousCatalog + "," + Published() + "]");
        Assert.Equal("NVIDIA GPU", Assert.Single(needed).Title);
        Assert.Equal("NVIDIA GPU", Assert.Single(published).Name);
        var vendor = Assert.Single(LenovoApplicableUpdateParser.ToVendorChecks(needed));
        Assert.DoesNotContain("PRIVATE_USER", vendor.Title + vendor.Evidence);
        Assert.False(vendor.IsInstallable);
    }
}
