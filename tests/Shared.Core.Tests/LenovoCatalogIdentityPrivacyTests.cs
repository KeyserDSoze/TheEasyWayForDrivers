using System.Text.Json;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #457: Lenovo.Client.Scripting catalog Type/ID are identities, not
/// display fields. MachineType catalog metadata does not attest
/// applicability or driver installation eligibility.
/// </summary>
public sealed class LenovoCatalogIdentityPrivacyTests
{
    private static string Catalog(
        object? id = null, object? type = null,
        string name = "NVIDIA graphics", string category = "Graphics",
        string version = "1.0", string date = "2026-11-15") =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["ID"] = id ?? "NVIDIA-GPU",
            ["PackageType"] = type ?? "2",
            ["Name"] = name,
            ["Category"] = category,
            ["Version"] = version,
            ["ReleaseDate"] = date
        });

    [Theory]
    [InlineData("2\n")]
    [InlineData("2\r")]
    [InlineData("2\t")]
    [InlineData("2\u202E")]
    [InlineData("\u00A02")]
    [InlineData("2\u200B")]
    [InlineData("2.0")]
    [InlineData("Driver")]
    [InlineData("3")]
    [InlineData("22")]
    public void InvalidDriverTypeIsNotNormalizedIntoOfficialCodeTwo(
        string malformed)
    {
        Assert.Empty(LenovoCatalogParser.Parse(Catalog(type: malformed)));
    }

    [Fact]
    public void NumericTypeTwoAndAsciiSpacePaddingAreSupported()
    {
        Assert.Equal("NVIDIA-GPU", Assert.Single(
            LenovoCatalogParser.Parse(Catalog(type: 2))).Id);
        Assert.Equal("NVIDIA-GPU", Assert.Single(
            LenovoCatalogParser.Parse(Catalog(type: " 2 "))).Id);
        Assert.Empty(LenovoCatalogParser.Parse(Catalog(type: false)));
    }

    [Theory]
    [InlineData("driver\n")]
    [InlineData("driver\t")]
    [InlineData(@"C:\PRIVATE_OEM\driver.inf")]
    [InlineData("sp\u202E123")]
    [InlineData("owner@example.invalid")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0")]
    [InlineData("id/serial")]
    public void MalformedOrPrivateIdsAreNeverDriverIdentity(string id)
    {
        Assert.Empty(LenovoCatalogParser.Parse(Catalog(id: id)));
        var direct = new LenovoCatalogParser.Package(
            id, "Driver", "1", "Graphics", "2026-11-15");
        Assert.Throws<InvalidDataException>(() =>
            DiscoveryResultAdapter.FromLenovo([direct]));
        Assert.Throws<InvalidDataException>(() =>
            OemPackageAssessment.FromLenovo(direct));
    }

    [Fact]
    public void LongIdsAreRejectedBeforeDeduplicatingTheOriginalTokens()
    {
        var overLimit = new string('A', 160) + "B";
        var valid = new string('A', 160);
        var payload = "[" + Catalog(overLimit) + "," +
            Catalog("NVIDIA-GPU") + "," + Catalog(valid) + "]";
        var parsed = LenovoCatalogParser.Parse(payload);
        Assert.Equal(2, parsed.Count);
        Assert.DoesNotContain(parsed, entry => entry.Id == overLimit);
        Assert.Contains(parsed, entry => entry.Id == valid);
        Assert.Contains(parsed, entry => entry.Id == "NVIDIA-GPU");
        Assert.Throws<InvalidDataException>(() =>
            DiscoveryResultAdapter.FromLenovo([
                new LenovoCatalogParser.Package(
                    overLimit, "Graphics", "1", "Graphics", "2026")
            ]));
    }

    [Theory]
    [InlineData("http://example.invalid/track")]
    [InlineData(@"C:\Users\PRIVATE_USER\driver.sys")]
    [InlineData("owner@example.invalid")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL")]
    [InlineData("SUBSYS_16A217AA")]
    [InlineData("C:/Users/PRIVATE_USER/driver.inf")]
    public void PrivateTailOutsideDisplayBudgetIsRedacted(string privateTail)
    {
        var longText = new string('A', 170) + privateTail;
        var parsed = Assert.Single(LenovoCatalogParser.Parse(
            Catalog(name: longText, category: longText,
                version: longText, date: longText)));
        var discovery = Assert.Single(
            DiscoveryResultAdapter.FromLenovo([parsed]));
        Assert.Equal("[dato riservato]", parsed.Name);
        Assert.Equal("[dato riservato]", discovery.Title);
        Assert.Equal("[dato riservato]", discovery.Version);
        Assert.Contains("Categoria Lenovo: [dato riservato]",
            discovery.Evidence);
        Assert.DoesNotContain(privateTail, discovery.Evidence);
        Assert.DoesNotContain("PRIVATE_", discovery.Evidence);
        Assert.Equal("Solo catalogo · non installabile",
            discovery.VerificationLabel);
    }

    [Fact]
    public void PublicPackageBoundaryRevalidatesMetadataAndId()
    {
        var text = new string('A', 180) + @"C:\Users\PRIVATE_USER";
        var direct = new LenovoCatalogParser.Package(
            " GPU-01 ", text, text, text, text);
        var result = Assert.Single(
            DiscoveryResultAdapter.FromLenovo([direct]));
        Assert.Equal("GPU-01", result.Identifier);
        Assert.Equal("[dato riservato]", result.Title);
        Assert.Equal("[dato riservato]", result.Version);
        Assert.DoesNotContain("PRIVATE_", result.Evidence);
        var assessment = OemPackageAssessment.FromLenovo(direct);
        Assert.Equal("GPU-01", assessment.PackageId);
        Assert.False(assessment.IsInstallable);
        Assert.False(assessment.SignatureVerified);
        Assert.False(assessment.DeviceMatchVerified);
    }

    [Fact]
    public void ReadableUnicodeIsRetainedButBidiAndControlsAreRemoved()
    {
        var parsed = Assert.Single(LenovoCatalogParser.Parse(
            Catalog(name: "Café réseau\u202E \n Audio",
                category: "Réteau")));
        Assert.Contains("Café réseau", parsed.Name);
        Assert.Contains("Audio", parsed.Name);
        Assert.False(parsed.Name.Contains('\u202E'));
        Assert.DoesNotContain("\n", parsed.Name);
        Assert.Equal("Réteau", parsed.Category);
    }

    [Fact]
    public void RareNvidiaAndKillerRemainAfterDominantOrInvalidRows()
    {
        var audio = Enumerable.Range(0, 800)
            .Select(i => Catalog($"Audio-{i:D4}", "2",
                "Audio driver", "Audio"));
        var invalid = Enumerable.Range(0, 40)
            .Select(i => Catalog($"BIOS-{i:D4}", "2\n",
                "Firmware", "Firmware"));
        var last = new[]
        {
            Catalog("NVIDIA-GPU", 2, "RTX 4060", "Graphics"),
            Catalog("Intel-Killer", "2", "Killer Wi-Fi", "Network")
        };
        var parsed = LenovoCatalogParser.Parse("[" + string.Join(",",
            audio.Concat(invalid).Concat(last)) + "]");
        Assert.Equal(LenovoCatalogParser.MaximumDriverResults,
            parsed.Count);
        Assert.Contains(parsed, entry => entry.Id == "NVIDIA-GPU");
        Assert.Contains(parsed, entry => entry.Id == "Intel-Killer");
        Assert.DoesNotContain(parsed, entry => entry.Name == "Firmware");
    }
}
