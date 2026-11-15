using System.Text.Json;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #459: HP CMSL lists published SoftPaq metadata, not an installable
/// driver or confirmed device applicability. Public/legacy records must
/// pass the same identity and privacy boundary as the JSON parser.
/// </summary>
public sealed class HpCmslIdentityPrivacyBoundaryTests
{
    private static string Json(
        string id = "sp12345", string name = "NVIDIA grafica",
        string category = "Driver - Graphics", string version = "32.1",
        string releaseType = "Recommended", string releaseDate = "2026-11-15") =>
        JsonSerializer.Serialize(new
        {
            Id = id, Name = name, Category = category,
            Version = version, ReleaseType = releaseType,
            ReleaseDate = releaseDate
        });

    private static HpSoftpaqEntry Direct(
        string id = "sp12345", string title = "NVIDIA grafica",
        string version = "32.1", string type = "Recommended",
        string date = "2026-11-15") =>
        new(id, title, version, type, date);

    [Theory]
    [InlineData(@"C:\Users\PRIVATE_NAME\driver.inf")]
    [InlineData("C:/Users/PRIVATE_NAME/driver.inf")]
    [InlineData("/home/private-user/customer-data")]
    [InlineData("https://oem.invalid/?serial=PRIVATE_SERIAL")]
    [InlineData("owner@example.invalid")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\INSTANCE")]
    [InlineData("SUBSYS_16A217AA")]
    [InlineData("SERIALNUMBER12345")]
    public void JsonMetadataPrivateTailIsRedactedBeforePreview(
        string privateText)
    {
        var raw = new string('A', 175) + privateText;
        var entry = Assert.Single(HpSoftpaqCatalogParser.Parse(
            Json(name: raw, version: raw,
                releaseType: raw, releaseDate: raw)));

        Assert.Equal("[dato riservato]", entry.Title);
        Assert.Equal("[dato riservato]", entry.Version);
        Assert.Equal("[dato riservato]", entry.ReleaseType);
        Assert.Equal("[dato riservato]", entry.PublishedDate);

        var discovery = Assert.Single(
            DiscoveryResultAdapter.FromHp([entry]));
        Assert.Equal("[dato riservato]", discovery.Title);
        Assert.Equal("[dato riservato]", discovery.Version);
        Assert.DoesNotContain(privateText, discovery.Evidence,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_", discovery.Evidence,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Solo catalogo · non installabile",
            discovery.VerificationLabel);
    }

    [Theory]
    [InlineData(@"C:\Users\PRIVATE_NAME\private.inf")]
    [InlineData("owner@example.invalid")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    [InlineData("https://oem.invalid/customer")]
    public void DirectSoftpaqCannotBypassDiscoveryPrivacy(string secret)
    {
        var raw = new string('Z', 180) + secret;
        var direct = Direct(id: " sp-2026.1 ", title: raw,
            version: raw, type: raw, date: raw);
        var result = Assert.Single(DiscoveryResultAdapter.FromHp([direct]));

        Assert.Equal("sp-2026.1", result.Identifier);
        Assert.Equal("[dato riservato]", result.Title);
        Assert.Equal("[dato riservato]", result.Version);
        Assert.DoesNotContain(secret, result.Evidence,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_", result.Evidence,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("sp-2026.1",
            OemPackageAssessment.FromHp(direct).PackageId);
        Assert.False(OemPackageAssessment.FromHp(direct).IsInstallable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sp123\t")]
    [InlineData("sp123\n")]
    [InlineData("sp123\u202E")]
    [InlineData("sp123/extra")]
    [InlineData("owner@example.invalid")]
    [InlineData(@"C:\Users\PRIVATE\sp123")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0")]
    public void DirectInvalidSoftpaqIdentitiesFailClosed(string id)
    {
        var direct = Direct(id: id);
        Assert.Throws<InvalidDataException>(() =>
            DiscoveryResultAdapter.FromHp([direct]));
        Assert.Throws<InvalidDataException>(() =>
            OemPackageAssessment.FromHp(direct));
        Assert.Empty(HpSoftpaqCatalogParser.Parse(Json(id: id)));
    }

    [Fact]
    public void OverlongIdentityIsRejectedInsteadOfTruncated()
    {
        var overLimit = new string('A', 150) + "B";
        Assert.Empty(HpSoftpaqCatalogParser.Parse(Json(id: overLimit)));
        Assert.Throws<InvalidDataException>(() =>
            DiscoveryResultAdapter.FromHp([Direct(id: overLimit)]));
        Assert.Equal(new string('A', 150), Assert.Single(
            DiscoveryResultAdapter.FromHp([
                Direct(id: new string('A', 150))
            ])).Identifier);
    }

    [Fact]
    public void UnicodeDescriptionStillReadableAndBidiRemoved()
    {
        var row = Assert.Single(HpSoftpaqCatalogParser.Parse(
            Json(name: "Gráfica réseau\u202E \n Driver",
                category: "Driver - Network",
                version: "2.3\u2066")));
        Assert.Contains("Gráfica réseau", row.Title);
        Assert.Contains("Driver", row.Title);
        Assert.False(row.Title.Contains('\u202E'));
        Assert.False(row.Title.Contains('\n'));
        Assert.False(row.Version.Contains('\u2066'));
        var direct = Assert.Single(
            DiscoveryResultAdapter.FromHp([row]));
        Assert.False(direct.Title.Contains('\u202E'));
    }

    [Fact]
    public void RareGpuNetworkAndAudioStayInFairPreview()
    {
        var dominant = Enumerable.Range(0, 600)
            .Select(i => Json($"sp-key{i:D4}", "Keyboard",
                "Driver - Keyboard, Mouse and Input Devices"));
        var tail = new[]
        {
            Json("sp-nvidia", "NVIDIA RTX 4060",
                "Driver - Graphics"),
            Json("sp-killer", "Intel Killer Wi-Fi",
                "Driver - Network"),
            Json("sp-realtek", "Realtek audio",
                "Driver - Audio")
        };
        var parsed = HpSoftpaqCatalogParser.Parse("[" + string.Join(",",
            dominant.Concat(tail)) + "]");
        Assert.Equal(HpSoftpaqCatalogParser.MaximumDriverResults,
            parsed.Count);
        Assert.Contains(parsed, row => row.Id == "sp-nvidia");
        Assert.Contains(parsed, row => row.Id == "sp-killer");
        Assert.Contains(parsed, row => row.Id == "sp-realtek");
        var result = DiscoveryResultAdapter.FromHp(parsed);
        Assert.All(result, row =>
        {
            Assert.Equal("Solo catalogo · non installabile",
                row.VerificationLabel);
            Assert.Null(row.HardwareId);
        });
    }

    [Fact]
    public void MissingOrPrivateDirectTitleDoesNotProduceTrustedRow()
    {
        Assert.Throws<InvalidDataException>(() =>
            DiscoveryResultAdapter.FromHp([Direct(title: "")]));
        Assert.Throws<InvalidDataException>(() =>
            OemPackageAssessment.FromHp(Direct(title: " ")));
        var entry = Assert.Single(DiscoveryResultAdapter.FromHp([
            Direct(title: @"C:\PRIVATE_USER\driver.inf")
        ]));
        Assert.Equal("[dato riservato]", entry.Title);
    }
}
