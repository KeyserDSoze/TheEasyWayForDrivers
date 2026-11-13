using System.Text.Json;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #442: the Lenovo official-module JSON parser enforces the same full-
/// response budget as its PowerShell caller. Safe identity is separate
/// from sanitized, localized display metadata; no OEM row is installable.
/// </summary>
public sealed class LenovoApplicableInputIntegrityTests
{
    private static string Row(string id, string title = "NVIDIA graphics",
        string type = "Driver", string category = "Graphics") =>
        JsonSerializer.Serialize(new
        {
            PackageID = id, Title = title, Type = type, Category = category,
            IsApplicable = true, IsInstalled = false, Version = "32.1"
        });

    [Fact]
    public void ThousandApplicableDriverRowsRespectTheExistingEightyRowPreview()
    {
        var items = Enumerable.Range(0, 1000)
            .Select(i => Row($"n{i:D5}", category: i == 999 ? "Network" : "Audio"));
        var results = LenovoApplicableUpdateParser.Parse(
            "[" + string.Join(",", items) + "]");

        Assert.Equal(LenovoApplicableUpdateParser.MaximumDriverResults,
            results.Count);
        Assert.Contains(results, x => x.PackageId == "n00999");
        Assert.All(LenovoApplicableUpdateParser.ToVendorChecks(results),
            check => Assert.False(check.IsInstallable));
    }

    [Fact]
    public void OverThousandSourceRowsFailExplicitlyEvenIfMostAreNotDrivers()
    {
        var irrelevant = Enumerable.Range(0, 1000)
            .Select(i => Row($"bios{i}", type: "BIOS"));
        var json = "[" + string.Join(",",
            irrelevant.Append(Row("nvidia", category: "Graphics"))) + "]";

        Assert.Throws<InvalidDataException>(() =>
            LenovoApplicableUpdateParser.Parse(json));
    }

    [Fact]
    public void HundredsOfNonDriversDoNotHideLateNvidiaKillerRealtek()
    {
        var bios = Enumerable.Range(0, 180)
            .Select(i => Row($"bios{i}", type: "BIOS"));
        var needed = new[]
        {
            Row("nvidia", category: "Graphics"),
            Row("killer", "Killer Wi-Fi", category: "Network"),
            Row("realtek", "Realtek Audio", category: "Audio")
        };
        var parsed = LenovoApplicableUpdateParser.Parse(
            "[" + string.Join(",", bios.Concat(needed)) + "]");

        Assert.Equal(3, parsed.Count);
        Assert.Contains(parsed, x => x.PackageId == "nvidia");
        Assert.Contains(parsed, x => x.PackageId == "killer");
        Assert.Contains(parsed, x => x.PackageId == "realtek");
    }

    [Theory]
    [InlineData("n3y001\t")]
    [InlineData("n3y001\n")]
    [InlineData("n3y001\u202E")]
    [InlineData("n3y001\u200B")]
    [InlineData("n3y001/part")]
    [InlineData("C:\\Users\\Private\\n3y001")]
    [InlineData("https://private.example/n3y001")]
    [InlineData("n3y001;unsafe")]
    [InlineData("n3y001 duplicate")]
    public void UntrustedPackageIdCannotBecomeVendorEvidence(string invalid)
    {
        var json = "[" + Row(invalid, "Bad entry") + "," +
            Row("n3y001", "Killer Wi-Fi") + "]";
        var parsed = LenovoApplicableUpdateParser.Parse(json);

        var valid = Assert.Single(parsed);
        Assert.Equal("n3y001", valid.PackageId);
        Assert.Equal("Killer Wi-Fi", valid.Title);
        var shown = Assert.Single(
            LenovoApplicableUpdateParser.ToVendorChecks(parsed));
        Assert.DoesNotContain("PRIVATE", shown.Evidence,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("n3y001 duplicate", shown.Evidence);
        Assert.False(shown.IsInstallable);
    }

    [Fact]
    public void LongIdentifiersAreRejectedInsteadOfTruncatedIntoValidKeys()
    {
        var longId = new string('A', 161);
        var maxLength = new string('B', 160);
        var parsed = LenovoApplicableUpdateParser.Parse(
            "[" + Row(longId) + "," + Row(maxLength) + "]");
        var actual = Assert.Single(parsed);
        Assert.Equal(maxLength, actual.PackageId);
        Assert.DoesNotContain(longId, actual.PackageId);
    }

    [Fact]
    public void NumericIdAndHarmlessAsciiSpacePaddingRemainReadable()
    {
        const string json = """
            [
              {"PackageID":12345,"Title":"Network","Type":2,
               "IsApplicable":true,"IsInstalled":false},
              {"PackageID":" n3y10.2_2026-1 ","Title":"GPU",
               "Category":"Driver","IsApplicable":true,"IsInstalled":false}
            ]
            """;
        var result = LenovoApplicableUpdateParser.Parse(json);
        Assert.Equal(2, result.Count);
        Assert.Equal("12345", result[0].PackageId);
        Assert.Equal("n3y10.2_2026-1", result[1].PackageId);
    }

    [Theory]
    [InlineData("Driver\n")]
    [InlineData("Driver\r")]
    [InlineData("Driver\t")]
    [InlineData("Driver\u202E")]
    [InlineData("BIOS")]
    [InlineData("Firmware")]
    public void ExplicitInvalidTypeNeverFallsBackToDriverCategory(
        string invalidType)
    {
        var json = "[" + Row("bad", type: invalidType, category: "Driver") +
            "," + Row("good", type: "Driver", category: "Network") + "]";
        var result = Assert.Single(LenovoApplicableUpdateParser.Parse(json));
        Assert.Equal("good", result.PackageId);
    }

    [Theory]
    [InlineData("Driver\n")]
    [InlineData("Driver\t")]
    [InlineData("Driver\u200B")]
    [InlineData("Driver\u00A0")]
    [InlineData("Firmware")]
    public void InvalidFallbackCategoryCannotBeRepairedIntoDriver(
        string category)
    {
        var json = JsonSerializer.Serialize(new
        {
            PackageID = "bad", Title = "Test", Category = category,
            IsApplicable = true, IsInstalled = false
        });
        Assert.Empty(LenovoApplicableUpdateParser.Parse(json));
    }

    [Fact]
    public void DisplayFieldsKeepInternationalNamesButRemoveInvisibleControls()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                PackageID = "n3y20", Title = "Gráfica\u202E àudio\u200B\t Wi-Fi",
                Type = "Driver", Category = "Graphics",
                IsApplicable = true, IsInstalled = false,
                Version = "3.1\u2066.0", Severity = "Critical\u202E",
                ReleaseDate = "2026-11-13\r\n"
            }
        });
        var result = Assert.Single(LenovoApplicableUpdateParser.Parse(json));
        Assert.Contains("Gráfica", result.Title);
        Assert.Contains("àudio", result.Title);
        Assert.Contains("Critical", result.Severity);
        Assert.Contains("2026-11-13", result.ReleaseDate);
        foreach (var field in new[]
            { result.Title, result.Version, result.Severity, result.ReleaseDate })
        {
            Assert.DoesNotContain(field, char.IsControl);
            Assert.DoesNotContain(field, ch =>
                char.GetUnicodeCategory(ch) is
                    System.Globalization.UnicodeCategory.Format or
                    System.Globalization.UnicodeCategory.LineSeparator or
                    System.Globalization.UnicodeCategory.ParagraphSeparator);
        }
        Assert.False(Assert.Single(
            LenovoApplicableUpdateParser.ToVendorChecks([result]))
            .IsInstallable);
    }

    [Fact]
    public void LongLocalizedDisplayMetadataRemainsBounded()
    {
        var json = Row("n3y24", new string('Ä', 200) + "\u202E");
        var parsed = Assert.Single(LenovoApplicableUpdateParser.Parse(json));
        Assert.Equal(160, parsed.Title.Length);
        Assert.DoesNotContain("\u202E", parsed.Title);
    }
}
