using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class HpImageAssistantReportParserTests
{
    [Fact]
    public void ReadsOnlyDriverRecommendationsFromHpiaXml()
    {
        // Simplified, anonymized shape of HPIA's published report:
        // HPIA / Recommendations / Drivers / Recommendation / Solution / Softpaq.
        const string xml = """
            <?xml version="1.0"?>
            <HPIA>
              <Version>1</Version>
              <SystemInfo><System><SerialNumber>do-not-use</SerialNumber></System></SystemInfo>
              <Summary><Drivers><Recommended>2</Recommended></Drivers></Summary>
              <Recommendations>
                <BIOS><Recommendation><Solution><Softpaq>
                  <Id>sp150010</Id><Name>BIOS image</Name>
                </Softpaq></Solution></Recommendation></BIOS>
                <Firmware><Recommendation><Solution><Softpaq>
                  <Id>sp150011</Id><Name>Dock firmware</Name>
                </Softpaq></Solution></Recommendation></Firmware>
                <Software><Recommendation><Solution><Softpaq>
                  <Id>sp150012</Id><Name>HP Utility</Name>
                </Softpaq></Solution></Recommendation></Software>
                <Drivers>
                  <Recommendation>
                    <TargetComponent>Intel wireless adapter</TargetComponent>
                    <TargetVersion>22.6.1</TargetVersion>
                    <ReferenceVersion>23.8.1</ReferenceVersion>
                    <Comments>HP update recommended</Comments>
                    <Solution><Softpaq><Id>sp155551</Id>
                      <Name>Intel Wi-Fi driver</Name><Version>23.8.1</Version>
                    </Softpaq></Solution>
                  </Recommendation>
                  <Recommendation>
                    <TargetVersion>Not installed</TargetVersion>
                    <Solution><Softpaq><Id>sp155552</Id>
                      <Name>NVIDIA RTX graphics driver</Name><Version>32.0.15.1234</Version>
                    </Softpaq></Solution>
                  </Recommendation>
                </Drivers>
              </Recommendations>
            </HPIA>
            """;

        var result = HpImageAssistantReportParser.Parse(xml);

        Assert.Equal(2, result.Count);
        Assert.Equal("sp155551", result[0].SoftPaqId);
        Assert.Equal("Intel Wi-Fi driver", result[0].Title);
        Assert.Equal("22.6.1", result[0].InstalledVersion);
        Assert.Equal("23.8.1", result[0].AvailableVersion);
        Assert.Equal("HP update recommended", result[0].HpComment);
        Assert.Equal("sp155552", result[1].SoftPaqId);
        Assert.Equal("32.0.15.1234", result[1].AvailableVersion);
        Assert.DoesNotContain(result, entry =>
            entry.Title.Contains("BIOS") || entry.Title.Contains("firmware"));
    }

    [Fact]
    public void EmptyDriverSectionIsNotARecommendation()
    {
        const string xml = """
            <HPIA><Recommendations>
              <Software><Recommendation><Solution><Softpaq>
                <Id>sp100010</Id><Name>Utility</Name>
              </Softpaq></Solution></Recommendation></Software>
              <Drivers/>
            </Recommendations></HPIA>
            """;
        Assert.Empty(HpImageAssistantReportParser.Parse(xml));
        Assert.Empty(HpImageAssistantReportParser.Parse(
            "<HPIA><Recommendations/></HPIA>"));
    }

    [Fact]
    public void DriverSoftPaqsAreDeduplicated()
    {
        const string xml = """
            <HPIA><Recommendations><Drivers>
              <Recommendation><Solution><Softpaq>
                <Id>sp12345</Id><Name>Audio driver</Name>
              </Softpaq></Solution></Recommendation>
              <Recommendation><Solution><Softpaq>
                <Id>SP12345</Id><Name>Duplicate package</Name>
              </Softpaq></Solution></Recommendation>
            </Drivers></Recommendations></HPIA>
            """;
        Assert.Equal("sp12345",
            Assert.Single(HpImageAssistantReportParser.Parse(xml)).SoftPaqId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("<Results><Recommendations/></Results>")]
    [InlineData("<HPIA><Summary><Drivers><Recommended>3</Recommended></Drivers></Summary></HPIA>")]
    [InlineData("<HPIA><Recommendations><Drivers><Recommendation/></Drivers></Recommendations></HPIA>")]
    [InlineData("<HPIA><Recommendations><Drivers><Recommendation><Solution><Softpaq><Id>bad</Id><Name>fake</Name></Softpaq></Solution></Recommendation></Drivers></Recommendations></HPIA>")]
    [InlineData("<HPIA><Recommendations><Drivers><Recommendation><Solution><Softpaq><Id>sp12345</Id></Softpaq></Solution></Recommendation></Drivers></Recommendations></HPIA>")]
    [InlineData("<HPIA><Recommendations><Drivers><Unrecognized/></Drivers></Recommendations></HPIA>")]
    [InlineData("<!DOCTYPE HPIA [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><HPIA><Recommendations>&x;</Recommendations></HPIA>")]
    public void NeverInterpretInvalidXmlAsNoUpdates(string xml) =>
        Assert.ThrowsAny<Exception>(() =>
            HpImageAssistantReportParser.Parse(xml));

    [Fact]
    public void ReportSizeAndCountAreLimited()
    {
        Assert.Throws<InvalidDataException>(() =>
            HpImageAssistantReportParser.Parse(new string('x', 4 * 1024 * 1024 + 1)));

        var packages = string.Concat(Enumerable.Range(10000, 151).Select(n =>
            "<Recommendation><Solution><Softpaq><Id>sp" + n +
            "</Id><Name>Driver</Name></Softpaq></Solution></Recommendation>"));
        Assert.Throws<InvalidDataException>(() =>
            HpImageAssistantReportParser.Parse(
                "<HPIA><Recommendations><Drivers>" + packages +
                "</Drivers></Recommendations></HPIA>"));
    }

    [Fact]
    public void HpiaDeviceRecommendationsNeverBecomeInstallableWuaUpdates()
    {
        var package = new HpImageAssistantReportParser.Package(
            "sp151122", "NVIDIA laptop driver", "31.2", "32.0",
            "HP update recommended");
        var check = Assert.Single(
            HpImageAssistantReportParser.ToVendorChecks([package]));

        Assert.Equal("HP", check.Vendor);
        Assert.True(check.IsCatalogMetadata);
        Assert.True(check.IsVendorApplicable);
        Assert.Equal(0, check.WuaMatches);
        Assert.Equal("31.2", check.InstalledVersion);
        Assert.Equal("32.0", check.PublishedVersion);
        Assert.Contains("31.2", check.Detail);
        Assert.Contains("32.0", check.Detail);
        Assert.Contains("non installabile", check.StatusText);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(check.OfficialUrl));
    }
    [Theory]
    [InlineData("sp12345\n")]
    [InlineData("sp12345\r")]
    [InlineData("sp12345\t")]
    [InlineData("sp12345\u202E")]
    [InlineData("sp12345\u200B")]
    [InlineData("sp１２３４５")]
    [InlineData("sp1234567890")]
    [InlineData("sp12345/other")]
    public void MalformedSoftPaqIdCannotBeRepairedIntoValidIdentity(
        string badId)
    {
        var xml =
            "<HPIA><Recommendations><Drivers><Recommendation>" +
            "<Solution><Softpaq><Id>" + badId + "</Id>" +
            "<Name>NVIDIA GPU driver</Name></Softpaq></Solution>" +
            "</Recommendation></Drivers></Recommendations></HPIA>";
        Assert.Throws<InvalidDataException>(() =>
            HpImageAssistantReportParser.Parse(xml));
    }

    [Fact]
    public void HarmlessAsciiPaddingPreservesRealHpSoftpaqIdentity()
    {
        const string xml = """
            <HPIA><Recommendations><Drivers><Recommendation>
              <Solution><Softpaq><Id> sp12345 </Id>
                <Name>NVIDIA GPU driver</Name></Softpaq></Solution>
            </Recommendation></Drivers></Recommendations></HPIA>
            """;
        Assert.Equal("sp12345",
            Assert.Single(HpImageAssistantReportParser.Parse(xml)).SoftPaqId);
    }

    [Fact]
    public void UnicodeTitlesRemainReadableWithoutBidiOrControlCharacters()
    {
        var xml = "<HPIA><Recommendations><Drivers><Recommendation>" +
            "<TargetVersion>31.2\u200B.0</TargetVersion>" +
            "<ReferenceVersion>32.0\u2066.5</ReferenceVersion>" +
            "<Comments>Raccomandato\u202E per la GPU\t</Comments>" +
            "<Solution><Softpaq><Id>sp12345</Id>" +
            "<Name>Gráfica NVIDIA\u202E àudio\u200B driver</Name>" +
            "</Softpaq></Solution></Recommendation>" +
            "</Drivers></Recommendations></HPIA>";
        var row = Assert.Single(HpImageAssistantReportParser.Parse(xml));

        Assert.Contains("Gráfica NVIDIA", row.Title);
        Assert.Contains("àudio", row.Title);
        Assert.Contains("Raccomandato", row.HpComment);
        Assert.Contains("31.2", row.InstalledVersion);
        Assert.Contains("32.0", row.AvailableVersion);
        foreach (var field in new[]
        {
            row.Title, row.InstalledVersion, row.AvailableVersion, row.HpComment
        })
        {
            Assert.DoesNotContain(field, char.IsControl);
            Assert.DoesNotContain(field, char.IsSurrogate);
            Assert.DoesNotContain(field, ch =>
                char.GetUnicodeCategory(ch) is
                    System.Globalization.UnicodeCategory.Format or
                    System.Globalization.UnicodeCategory.LineSeparator or
                    System.Globalization.UnicodeCategory.ParagraphSeparator);
            Assert.True(field.Length <= 160);
        }
        var check = Assert.Single(
            HpImageAssistantReportParser.ToVendorChecks([row]));
        Assert.True(check.IsVendorApplicable);
        Assert.False(check.IsInstallable);
    }

    [Theory]
    [InlineData(@"C:\\Users\\PRIVATE_USER\\driver.inf")]
    [InlineData(@"PCI\\VEN_10DE&DEV_28A0\\PRIVATE_SERIAL")]
    [InlineData("https://intranet.example/private-order")]
    [InlineData("support@example.com")]
    [InlineData("PCI VEN_8086 and DEV_51F1")]
    [InlineData("C:/Users/PRIVATE_PC/driver.inf")]
    public void CurrentPcSensitiveCommentsAreRedactedBeforeOemDisplay(
        string privateReference)
    {
        var safeReference = System.Security.SecurityElement.Escape(
            privateReference)!;
        var xml =
            "<HPIA><Recommendations><Drivers><Recommendation>" +
            "<Comments>Check: " + safeReference + "</Comments>" +
            "<Solution><Softpaq><Id>sp12345</Id>" +
            "<Name>Intel Killer Wi-Fi</Name></Softpaq></Solution>" +
            "</Recommendation></Drivers></Recommendations></HPIA>";
        var parsed = Assert.Single(HpImageAssistantReportParser.Parse(xml));
        Assert.Equal("[dato riservato]", parsed.HpComment);

        var check = Assert.Single(
            HpImageAssistantReportParser.ToVendorChecks([parsed]));
        Assert.Equal("Intel Killer Wi-Fi", check.Title);
        Assert.Contains("[dato riservato]", check.Detail);
        Assert.DoesNotContain(privateReference, check.Detail);
        Assert.False(check.IsInstallable);
    }

    [Fact]
    public void PrivateReferenceBeyondDisplayCapIsStillFullyRedacted()
    {
        var padded = new string('A', 180) +
            @" C:\\Users\\PRIVATE_AFTER_LIMIT\\driver.inf";
        var xml =
            "<HPIA><Recommendations><Drivers><Recommendation>" +
            "<Solution><Softpaq><Id>sp12345</Id><Name>" + padded +
            "</Name></Softpaq></Solution>" +
            "</Recommendation></Drivers></Recommendations></HPIA>";
        var package = Assert.Single(HpImageAssistantReportParser.Parse(xml));
        Assert.Equal("[dato riservato]", package.Title);
        var check = Assert.Single(
            HpImageAssistantReportParser.ToVendorChecks([package]));
        Assert.DoesNotContain("PRIVATE_AFTER_LIMIT", check.Detail +
            check.Title);
    }

    [Fact]
    public void DirectPackageCallersCannotBypassPrivacySanitizer()
    {
        var injected = new HpImageAssistantReportParser.Package(
            "sp12345", "Realtek àudio\u202E driver",
            @"C:\\Users\\PRIVATE_LOCAL\\driver.inf", "2.5.0",
            @"USB\\VID_0BDA&PID_1234\\PRIVATE_DEVICE");
        var check = Assert.Single(
            HpImageAssistantReportParser.ToVendorChecks([injected]));

        Assert.Contains("Realtek àudio", check.Title);
        Assert.DoesNotContain('\u202E', check.Title);
        Assert.Contains("[dato riservato]", check.Detail);
        Assert.DoesNotContain("PRIVATE_LOCAL", check.Detail);
        Assert.DoesNotContain("PRIVATE_DEVICE", check.Detail);
        Assert.True(check.IsVendorApplicable);
        Assert.False(check.IsInstallable);
    }

    [Theory]
    [InlineData("sp12345\u202E")]
    [InlineData("sp１２３４５")]
    [InlineData("C:\\PRIVATE\\sp12345")]
    public void DirectPackageCallersCannotBypassSoftpaqIdentityCheck(
        string invalidId)
    {
        var invalid = new HpImageAssistantReportParser.Package(
            invalidId, "NVIDIA driver", "1.0", "2.0", "Recommended");
        Assert.Throws<InvalidDataException>(() =>
            HpImageAssistantReportParser.ToVendorChecks([invalid]));
    }

}
