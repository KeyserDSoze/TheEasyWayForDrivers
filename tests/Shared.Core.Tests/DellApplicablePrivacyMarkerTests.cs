using System.Security;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #558: a Dell DCU current-PC report may contain a local serial/order
/// token in valid-looking ASCII component IDs or Unicode-obscured
/// metadata. These are never installation authorizations.
/// </summary>
public sealed class DellApplicablePrivacyMarkerTests
{
    private static string Report(string id, string name = "Intel Killer Wi-Fi",
        string version = "24.1", string severity = "Recommended") =>
        "<Updates><Update type=\"Driver\" id=\"" + Escape(id) +
        "\" name=\"" + Escape(name) + "\" version=\"" +
        Escape(version) + "\" severity=\"" + Escape(severity) +
        "\" /></Updates>";

    private static string Escape(string input) => SecurityElement.Escape(input)!;

    [Theory]
    [InlineData("SERIAL_1234")]
    [InlineData("ORDER_987")]
    [InlineData("CUSTOMER_1234")]
    [InlineData("HOSTNAME_OFFICE")]
    [InlineData("USERNAME_LOCAL")]
    [InlineData("ACCOUNT_4815")]
    [InlineData("PASSWORD_TEST")]
    [InlineData("EMAIL_LOCAL")]
    [InlineData("SECRET_TOKEN")]
    [InlineData("DEVICEID_123")]
    [InlineData("UUID_1234")]
    [InlineData("PCI_VEN_10DE")]
    [InlineData("PCI_DEV_28A0")]
    [InlineData("SWC_MI_00")]
    [InlineData("sp_DRIVER_SUBSYS_1234")]
    public void PrivateAsciiComponentIdCannotBecomePublicDellIdentity(string id)
    {
        Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse(Report(id)));
        var direct = new DellApplicableUpdateParser.Package(
            id, "NVIDIA graphics", "1.0", "Recommended");
        Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.ToVendorChecks([direct]));
    }

    [Theory]
    [InlineData("SERIAL_9123")]
    [InlineData("ORDER_4815")]
    [InlineData("CUSTOMER_1234")]
    [InlineData("ACCOUNT_77")]
    [InlineData("HOSTNAME_PC")]
    [InlineData("PASSWORD_HINT")]
    [InlineData("EMAIL_OWNER")]
    [InlineData("INSTANCEID_1")]
    [InlineData("DEVICEID_ABC")]
    [InlineData("VEN_10DE")]
    [InlineData("PID_4321")]
    [InlineData("MI_00")]
    [InlineData("S\u200BERIAL_9123")]
    [InlineData("OR\u2060DER_4815")]
    [InlineData("CU\u202ESTOMER_1234")]
    public void FullDellDisplayFieldsRedactEvenObscuredPrivateMarkers(string secret)
    {
        var contaminated = new string('A', 170) + secret;
        var candidate = Assert.Single(DellApplicableUpdateParser.Parse(
            Report("dell-driver-123", contaminated,
                contaminated, contaminated)));
        Assert.Equal("[dato riservato]", candidate.Title);
        Assert.Equal("[dato riservato]", candidate.Version);
        Assert.Equal("[dato riservato]", candidate.Severity);

        var result = Assert.Single(
            DellApplicableUpdateParser.ToVendorChecks([candidate]));
        Assert.Equal("[dato riservato]", result.Title);
        Assert.Equal("[dato riservato]", result.PublishedVersion);
        Assert.DoesNotContain(secret, result.Evidence + result.Detail);
        Assert.DoesNotContain("PRIVATE_", result.Evidence + result.Detail);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void DirectPackageRevalidatesFieldsAgainstUnicodeInvisibleTokens()
    {
        var unsafeTitle = "OR\u200bDER_9001";
        var direct = new DellApplicableUpdateParser.Package(
            "driver-123", unsafeTitle,
            "CU\u2060STOMER_77", "SER\u202eIAL_4421");
        var shown = Assert.Single(
            DellApplicableUpdateParser.ToVendorChecks([direct]));
        Assert.Equal("[dato riservato]", shown.Title);
        Assert.Equal("[dato riservato]", shown.PublishedVersion);
        Assert.Contains("[dato riservato]", shown.Evidence);
        Assert.DoesNotContain("9001", shown.Title + shown.Evidence);
        Assert.DoesNotContain("4421", shown.Detail);
        Assert.False(shown.IsInstallable);
    }

    [Fact]
    public void PublicGuidAndOpaqueIdsAndInternationalTitlesRemainVisible()
    {
        foreach (var id in new[]
        {
            "driver-12345_abc.1",
            "{12345678-1234-1234-abcd-1234567890ab}"
        })
        {
            var candidate = Assert.Single(
                DellApplicableUpdateParser.Parse(
                    Report(id, "Réseau Intel Wi-Fi", "24.1", "Consigliato")));
            Assert.Equal(id, candidate.Id);
            Assert.Equal("Réseau Intel Wi-Fi", candidate.Title);
            var visible = Assert.Single(
                DellApplicableUpdateParser.ToVendorChecks([candidate]));
            Assert.Contains(id, visible.Evidence);
            Assert.False(visible.IsInstallable);
        }
    }

    [Fact]
    public void PrivateTitleDuplicateCanBeReplacedByLaterSafeSameId()
    {
        var xml = "<Updates>" +
            "<Update type=\"Driver\" id=\"driver-1\" " +
            "name=\"ORDER_12345\" />" +
            "<Update type=\"Driver\" id=\"DRIVER-1\" " +
            "name=\"Dell network driver\" />" +
            "<Update type=\"BIOS\" id=\"bios-1\" name=\"UEFI\" />" +
            "</Updates>";
        var result = Assert.Single(DellApplicableUpdateParser.Parse(xml));
        // When a redacted first title is replaced by a public duplicate,
        // the replacement retains its own original opaque ID casing.
        Assert.Equal("DRIVER-1", result.Id);
        Assert.Equal("Dell network driver", result.Title);
        Assert.False(Assert.Single(
            DellApplicableUpdateParser.ToVendorChecks([result])).IsInstallable);
    }
}
