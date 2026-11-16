using System.Security;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #532: local HP Image Assistant fields must not reveal current-PC
/// identifiers; a private first duplicate must not hide a later
/// public SoftPaq title that HPIA also reports.
/// </summary>
public sealed class HpiaSensitiveMarkerAndDuplicateTests
{
    private static string Xml(params string[] recommendations) =>
        "<HPIA><Recommendations><Drivers>" +
        string.Concat(recommendations) +
        "</Drivers></Recommendations></HPIA>";

    private static string Recommendation(string id, string name,
        string targetVersion = "31.0", string referenceVersion = "32.0",
        string comments = "HP recommended") =>
        "<Recommendation>" +
        "<TargetVersion>" + SecurityElement.Escape(targetVersion) + "</TargetVersion>" +
        "<ReferenceVersion>" + SecurityElement.Escape(referenceVersion) + "</ReferenceVersion>" +
        "<Comments>" + SecurityElement.Escape(comments) + "</Comments>" +
        "<Solution><Softpaq><Id>" + SecurityElement.Escape(id) + "</Id>" +
        "<Name>" + SecurityElement.Escape(name) + "</Name>" +
        "</Softpaq></Solution></Recommendation>";

    [Theory]
    [InlineData("ORDER_991234")]
    [InlineData("CUSTOMER_12345")]
    [InlineData("SERIAL_XYZ123")]
    [InlineData("SECRET_TOKEN123")]
    [InlineData("OR\u200bDER_991234")]
    [InlineData("CUST\u2066OMER_12345")]
    [InlineData("SER\u202cIAL_XYZ123")]
    [InlineData("PRI\u200bVATE_ORDER123")]
    public void CurrentPcIdentifiersAreRedactedInEveryDisplayField(
        string marker)
    {
        var report = Xml(Recommendation("sp12345",
            "NVIDIA driver " + marker,
            "installed " + marker, "offered " + marker,
            "HP note " + marker));

        var row = Assert.Single(HpImageAssistantReportParser.Parse(report));
        Assert.Equal("[dato riservato]", row.Title);
        Assert.Equal("[dato riservato]", row.InstalledVersion);
        Assert.Equal("[dato riservato]", row.AvailableVersion);
        Assert.Equal("[dato riservato]", row.HpComment);

        var check = Assert.Single(HpImageAssistantReportParser
            .ToVendorChecks([row]));
        Assert.Equal("[dato riservato]", check.Title);
        Assert.DoesNotContain("991234", check.Title + check.Detail);
        // The PUBLIC SoftPaq identity itself may legitimately contain
        // these digits; check the field redaction, not an ID substring.
        Assert.Contains("sp12345", check.Detail);
        Assert.DoesNotContain("XYZ123", check.Title + check.Detail);
        Assert.DoesNotContain("TOKEN123", check.Title + check.Detail);
        Assert.False(check.IsInstallable);
    }

    [Fact]
    public void PrivateMarkerAfterDisplayLimitStillRedactsEntireField()
    {
        var title = new string('A', 175) + " CUSTOMER_33211";
        var parsed = Assert.Single(HpImageAssistantReportParser.Parse(
            Xml(Recommendation("sp155551", title))));
        Assert.Equal("[dato riservato]", parsed.Title);
        var check = Assert.Single(HpImageAssistantReportParser
            .ToVendorChecks([parsed]));
        Assert.DoesNotContain("CUSTOMER_33211", check.Title + check.Detail);
    }

    [Fact]
    public void LaterPublicDuplicateReplacesPrivateTitleAndAllItsMetadata()
    {
        var xml = Xml(
            Recommendation("sp155551", "ORDER_12345 NVIDIA driver",
                "OLD_SECRET_123", "OLD_SECRET_456", "OLD_SECRET_789"),
            Recommendation("SP155551", "NVIDIA RTX 4060 laptop driver",
                "31.2", "32.0", "HP recommendation"));

        var row = Assert.Single(HpImageAssistantReportParser.Parse(xml));
        Assert.Equal("SP155551", row.SoftPaqId);
        Assert.Equal("NVIDIA RTX 4060 laptop driver", row.Title);
        Assert.Equal("31.2", row.InstalledVersion);
        Assert.Equal("32.0", row.AvailableVersion);
        Assert.Equal("HP recommendation", row.HpComment);
        var display = Assert.Single(HpImageAssistantReportParser
            .ToVendorChecks([row]));
        Assert.True(display.IsVendorApplicable);
        Assert.False(display.IsInstallable);
        Assert.DoesNotContain("OLD_", display.Title + display.Detail);
    }

    [Fact]
    public void FirstPublicDuplicateRetainsItsOwnVersionAndDescription()
    {
        var xml = Xml(
            Recommendation("sp155551", "Intel Wireless driver",
                "21.0", "22.0", "Original HP row"),
            Recommendation("SP155551", "NVIDIA display driver",
                "30.0", "33.0", "Different duplicate"));

        var row = Assert.Single(HpImageAssistantReportParser.Parse(xml));
        Assert.Equal("sp155551", row.SoftPaqId);
        Assert.Equal("Intel Wireless driver", row.Title);
        Assert.Equal("21.0", row.InstalledVersion);
        Assert.Equal("22.0", row.AvailableVersion);
        Assert.Equal("Original HP row", row.HpComment);
    }

    [Fact]
    public void DirectLegacyPackageCannotBypassSensitiveMarkerRedaction()
    {
        var package = new HpImageAssistantReportParser.Package(
            "sp12345", "Realtek audio driver", "SERIAL_123456",
            "ORDER_654321", "CUSTOMER_abcdef");
        var check = Assert.Single(HpImageAssistantReportParser
            .ToVendorChecks([package]));
        Assert.Equal("Realtek audio driver", check.Title);
        Assert.Equal("[dato riservato]", check.InstalledVersion);
        Assert.Equal("[dato riservato]", check.PublishedVersion);
        Assert.DoesNotContain("CUSTOMER_", check.Detail);
        Assert.DoesNotContain("ORDER_", check.Detail);
        Assert.DoesNotContain("SERIAL_", check.Detail);
        Assert.False(check.IsInstallable);
    }
}
