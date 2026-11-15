using System.Text.Json;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #458: Get-LnvUpdate reports data about the CURRENT PC. A bounded
/// presentation must not leak paths, serials or PnP instances, even
/// if private content appears after the 160-character preview limit.
/// Lenovo applicability remains advisory, never installation approval.
/// </summary>
public sealed class LenovoApplicablePrivacyBoundaryTests
{
    private static string Json(
        string id = "n3w1100", string title = "NVIDIA grafica",
        string version = "32.1", string releaseDate = "2026-11-15",
        string severity = "Critical", string category = "Graphics") =>
        JsonSerializer.Serialize(new
        {
            PackageID = id, Title = title, Type = "Driver",
            Category = category, IsApplicable = true,
            IsInstalled = false, Version = version,
            ReleaseDate = releaseDate, Severity = severity
        });

    private static LenovoApplicableUpdateParser.Package Direct(
        string id = "n3w1100", string title = "NVIDIA grafica",
        string version = "32.1", string date = "2026-11-15",
        string severity = "Critical") =>
        new(id, title, version, date, severity);

    [Theory]
    [InlineData(@"C:\Users\LOCAL_ACCOUNT\driver.inf")]
    [InlineData("C:/Users/LOCAL_ACCOUNT/driver.inf")]
    [InlineData("/home/local-user/private.inf")]
    [InlineData("https://example.invalid/?serial=PRIVATE_SERIAL")]
    [InlineData("owner@example.invalid")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL")]
    [InlineData("SUBSYS_16A217AA")]
    [InlineData("SERIALNUMBER99999")]
    public void JsonPrivateMetadataAfter160CharsIsRedactedBeforePreview(
        string secret)
    {
        var raw = new string('A', 170) + secret;
        var parsed = Assert.Single(LenovoApplicableUpdateParser.Parse(
            Json(title: raw, version: raw, releaseDate: raw,
                severity: raw, category: "Graphics")));

        Assert.Equal("[dato riservato]", parsed.Title);
        Assert.Equal("[dato riservato]", parsed.Version);
        Assert.Equal("[dato riservato]", parsed.ReleaseDate);
        Assert.Equal("[dato riservato]", parsed.Severity);

        var card = Assert.Single(
            LenovoApplicableUpdateParser.ToVendorChecks([parsed]));
        Assert.Equal("[dato riservato]", card.DeviceName);
        Assert.Equal("[dato riservato]", card.PublishedVersion);
        Assert.Contains("[dato riservato]", card.Evidence);
        Assert.DoesNotContain(secret, card.Evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_", card.Evidence,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(card.IsInstallable);
        Assert.True(card.IsVendorApplicable);
        Assert.True(card.IsCatalogMetadata);
        Assert.Equal(0, card.WuaMatches);
    }

    [Theory]
    [InlineData(@"C:\Users\LOCAL_ACCOUNT\driver.inf")]
    [InlineData("owner@example.invalid")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    [InlineData("https://example.invalid/local-pc")]
    public void DirectPublicPackagesCannotBypassRedaction(string secret)
    {
        var raw = new string('Z', 190) + secret;
        var check = Assert.Single(
            LenovoApplicableUpdateParser.ToVendorChecks([
                Direct(id: " legit-004 ", title: raw,
                    version: raw, date: raw, severity: raw)
            ]));

        Assert.Equal("[dato riservato]", check.DeviceName);
        Assert.Equal("[dato riservato]", check.PublishedVersion);
        Assert.Contains("ID pacchetto: legit-004", check.Evidence);
        Assert.DoesNotContain(secret, check.Evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("LOCAL_ACCOUNT", check.Detail,
            StringComparison.Ordinal);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(
            check.OfficialUrl));
        Assert.False(check.IsInstallable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("id\t")]
    [InlineData("id\n")]
    [InlineData("id\u202E")]
    [InlineData(@"C:\Users\PRIVATE_PC")]
    [InlineData("url/path")]
    [InlineData("owner@example.invalid")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0")]
    public void DirectInvalidPackageIdsFailClosed(string id)
    {
        Assert.Throws<InvalidDataException>(() =>
            LenovoApplicableUpdateParser.ToVendorChecks([Direct(id: id)]));
    }

    [Fact]
    public void DirectOverlongIdCannotBeShortenedIntoDifferentPackage()
    {
        var tooLong = new string('A', 160) + 'B';
        Assert.Throws<InvalidDataException>(() =>
            LenovoApplicableUpdateParser.ToVendorChecks([
                Direct(id: tooLong)
            ]));
        Assert.Single(LenovoApplicableUpdateParser.ToVendorChecks([
            Direct(id: new string('A', 160))
        ]));
    }

    [Fact]
    public void OrdinaryLenovoNvidiaAndKillerEvidenceRemainsVisibleAndReadOnly()
    {
        var rows = LenovoApplicableUpdateParser.Parse("[" + string.Join(",",
            Json("nvidia-gpu", "NVIDIA RTX 4060", "32.1", category: "Graphics"),
            Json("killer-wifi", "Intel Killer Wi-Fi", "23.1",
                severity: "Recommended", category: "Network")) + "]");
        Assert.Equal(2, rows.Count);
        var cards = LenovoApplicableUpdateParser.ToVendorChecks(rows);
        Assert.Equal(2, cards.Count);
        Assert.Contains(cards, c => c.DeviceName == "NVIDIA RTX 4060");
        Assert.Contains(cards, c => c.DeviceName == "Intel Killer Wi-Fi");
        Assert.All(cards, c =>
        {
            Assert.True(c.IsVendorApplicable);
            Assert.True(c.IsCatalogMetadata);
            Assert.False(c.IsInstallable);
            Assert.Equal(0, c.WuaMatches);
        });
    }

    [Fact]
    public void UnicodeLegitimateDisplayTextIsPreservedAndControlsAreStripped()
    {
        const string localized = "Gráfica réseau\u202E \n Audio";
        var package = Assert.Single(LenovoApplicableUpdateParser.Parse(
            Json(title: localized, version: "32.1\u2066")));
        Assert.Contains("Gráfica réseau", package.Title);
        Assert.Contains("Audio", package.Title);
        Assert.False(package.Title.Contains('\u202E'));
        Assert.False(package.Version.Contains('\u2066'));
        Assert.False(package.Title.Contains('\n'));
        var card = Assert.Single(
            LenovoApplicableUpdateParser.ToVendorChecks([package]));
        Assert.False(card.DeviceName.Contains('\u202E'));
        Assert.False(card.IsInstallable);
    }
}
