using System.Security;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #448: identity and private current-PC metadata from Dell Command
/// Update XML cannot be turned into fabricated identities or copied into
/// OEM cards. No DCU record may become an OmegaDrive install action.
/// </summary>
public sealed class DellApplicableReportIntegrityTests
{
    private static string Report(string id, string name = "NVIDIA RTX 4060",
        string version = "32.0.15.1000", string severity = "Recommended") =>
        "<Updates><Update type=\"Driver\" id=\"" + Escape(id) +
        "\" name=\"" + Escape(name) + "\" version=\"" + Escape(version) +
        "\" severity=\"" + Escape(severity) + "\" /></Updates>";

    private static string Escape(string value) =>
        SecurityElement.Escape(value)!;

    [Theory]
    [InlineData("driver12345")]
    [InlineData("driver-12345_abc.1")]
    [InlineData("{12345678-1234-1234-abcd-1234567890ab}")]
    public void ValidComponentIdsRemainExactAndReadOnly(string id)
    {
        var pkg = Assert.Single(DellApplicableUpdateParser.Parse(
            Report(id, "Intel Killer Wi-Fi")));
        Assert.Equal(id, pkg.Id);
        var check = Assert.Single(DellApplicableUpdateParser.ToVendorChecks([pkg]));
        Assert.Contains(id, check.Evidence);
        Assert.True(check.IsVendorApplicable);
        Assert.True(check.IsCatalogMetadata);
        Assert.False(check.IsInstallable);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(check.OfficialUrl));
    }

    [Theory]
    [InlineData("driver1000\n")]
    [InlineData("driver1000\r")]
    [InlineData("driver1000\t")]
    [InlineData("driver1000\u202E")]
    [InlineData("driver1000\u200B")]
    [InlineData(@"C:\Users\PRIVATE_USER\driver.inf")]
    [InlineData("https://private.example/driver")]
    [InlineData("driver 1000")]
    [InlineData("{invalid-guid}")]
    [InlineData("driver/extra")]
    public void InvalidIdCannotBecomeIdentityOrDedupKey(string invalid)
    {
        // Use immediate child text: XML normalizes whitespace in
        // attributes, but not element text. An ID is not display text.
        var xml = "<Updates><Update type=\"Driver\"><Id>" +
            Escape(invalid) + "</Id><Name>Realtek audio</Name></Update></Updates>";
        Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse(xml));
    }

    [Fact]
    public void OneHundredSixtyCharacterIdIsAllowedBut161IsNeverTruncated()
    {
        var valid = new string('A', 160);
        Assert.Equal(valid, Assert.Single(
            DellApplicableUpdateParser.Parse(Report(valid))).Id);
        Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse(Report(valid + "B")));
    }

    [Theory]
    [InlineData("Driver\n")]
    [InlineData("Driver\t")]
    [InlineData("Driver\u202E")]
    [InlineData("BIOS")]
    [InlineData("Firmware")]
    public void SpoofedDriverDiscriminatorIsNotAttested(string type)
    {
        var xml = "<Updates><Update><Type>" + Escape(type) +
            "</Type><Id>fake</Id><Name>Fake update</Name></Update></Updates>";
        Assert.Empty(DellApplicableUpdateParser.Parse(xml));
    }

    [Fact]
    public void MissingTypeAndNestedTypeOrIdFailClosed()
    {
        var examples = new[]
        {
            "<Updates><Update id=\"x\" name=\"NVIDIA\" /></Updates>",
            "<Updates><Update><Type><T>Driver</T></Type>" +
                "<Id>x</Id><Name>NVIDIA</Name></Update></Updates>",
            "<Updates><Update type=\"Driver\"><Id><I>x</I></Id>" +
                "<Name>NVIDIA</Name></Update></Updates>"
        };
        foreach (var xml in examples)
            Assert.Throws<InvalidDataException>(() =>
                DellApplicableUpdateParser.Parse(xml));
    }

    [Fact]
    public void InternationalNamesArePreservedAndUnicodeControlsRemoved()
    {
        var xml = Report("drv1",
            "Gráfica NVIDIA\u202E e àudio\u200B Wi-Fi",
            "31.2\u2066.0", "Raccomandato\u202E\n");
        var pkg = Assert.Single(DellApplicableUpdateParser.Parse(xml));
        Assert.Contains("Gráfica NVIDIA", pkg.Title);
        Assert.Contains("àudio", pkg.Title);
        foreach (var text in new[] {pkg.Title, pkg.Version, pkg.Severity})
        {
            Assert.True(text.Length <= 160);
            Assert.DoesNotContain(text, char.IsControl);
            Assert.DoesNotContain(text, char.IsSurrogate);
            Assert.DoesNotContain(text, ch =>
                char.GetUnicodeCategory(ch) is
                    System.Globalization.UnicodeCategory.Format or
                    System.Globalization.UnicodeCategory.LineSeparator or
                    System.Globalization.UnicodeCategory.ParagraphSeparator);
        }
        Assert.False(Assert.Single(
            DellApplicableUpdateParser.ToVendorChecks([pkg])).IsInstallable);
    }

    [Theory]
    [InlineData(@"C:\Users\PRIVATE_USER\driver.inf")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL")]
    [InlineData("support@example.com")]
    [InlineData("https://private.example/order")]
    [InlineData("PCI VEN_8086 DEV_51F1")]
    [InlineData("C:/Users/PRIVATE_PC/driver.inf")]
    public void PrivateReportFieldsAreRedactedBeforeOemDisplay(string secret)
    {
        var pkg = Assert.Single(DellApplicableUpdateParser.Parse(
            Report("driver1", "Intel Killer Wi-Fi", secret, secret)));
        Assert.Equal("[dato riservato]", pkg.Version);
        Assert.Equal("[dato riservato]", pkg.Severity);
        var check = Assert.Single(DellApplicableUpdateParser.ToVendorChecks([pkg]));
        Assert.DoesNotContain(secret, check.Detail);
        Assert.DoesNotContain(secret, check.PublishedVersion!);
        Assert.Contains("[dato riservato]", check.Detail);
    }

    [Fact]
    public void SensitiveContentAfterDisplayLimitRedactsWholeTitle()
    {
        var input = new string('A', 180) +
            @" C:\Users\PRIVATE_AFTER_CAP\driver.inf";
        var pkg = Assert.Single(DellApplicableUpdateParser.Parse(
            Report("driver1", input)));
        Assert.Equal("[dato riservato]", pkg.Title);
        var check = Assert.Single(DellApplicableUpdateParser.ToVendorChecks([pkg]));
        Assert.DoesNotContain("PRIVATE_AFTER_CAP", check.Detail + check.Title);
    }

    [Fact]
    public void DirectPackageCallersCannotBypassIdentityOrDisplayChecks()
    {
        var badId = new DellApplicableUpdateParser.Package(
            "C:\\PRIVATE\\driver.inf", "GPU", "1.0", "Recommended");
        Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.ToVendorChecks([badId]));

        var unsafeRow = new DellApplicableUpdateParser.Package(
            "driver1", "Realtek àudio\u202E",
            @"C:\Users\PRIVATE_LOCAL\driver.inf",
            @"USB\VID_0BDA&PID_1234\PRIVATE_DEVICE");
        var row = Assert.Single(
            DellApplicableUpdateParser.ToVendorChecks([unsafeRow]));
        Assert.Contains("Realtek àudio", row.Title);
        Assert.DoesNotContain('\u202E', row.Title);
        Assert.DoesNotContain("PRIVATE_LOCAL", row.Detail + row.PublishedVersion);
        Assert.DoesNotContain("PRIVATE_DEVICE", row.Detail);
        Assert.False(row.IsInstallable);
    }

    [Fact]
    public void FirmwareAndBiosRemainExcludedWithoutHidingLateDriver()
    {
        var leading = string.Concat(Enumerable.Range(0, 190)
            .Select(i => $"<Update type=\"{(i % 2 == 0 ? "BIOS" : "Firmware")}\" " +
                $"id=\"fw{i}\" name=\"Firmware\" />"));
        var xml = "<Updates>" + leading +
            "<Update type=\"Driver\" id=\"gpu-rtx\" name=\"NVIDIA GPU\" />" +
            "<Update type=\"Driver\" id=\"wifi-killer\" name=\"Intel Killer\" />" +
            "</Updates>";
        var rows = DellApplicableUpdateParser.Parse(xml);
        Assert.Equal(2, rows.Count);
        Assert.Equal("gpu-rtx", rows[0].Id);
        Assert.Equal("wifi-killer", rows[1].Id);
    }
}
