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
}
