using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DellApplicableUpdateParserTests
{
    [Fact]
    public void OnlyReturnsExplicitDriverEntries()
    {
        const string report = """
            <Updates>
              <Update id="gpu1" name="NVIDIA RTX 4060 driver" type="Driver"
                      version="32.0.16.2101" severity="Recommended" />
              <Update id="fw1" name="Firmware" type="Firmware" version="1" />
              <Update id="bios1" name="BIOS" type="BIOS" version="2" />
              <Update id="app1" name="SupportAssist" type="Application" version="3" />
              <Update id="gpu1" name="NVIDIA RTX 4060 duplicate" type="Driver" />
            </Updates>
            """;

        var result = Assert.Single(DellApplicableUpdateParser.Parse(report));
        Assert.Equal("gpu1", result.Id);
        Assert.Equal("NVIDIA RTX 4060 driver", result.Title);
        Assert.Equal("32.0.16.2101", result.Version);
        Assert.Equal("Recommended", result.Severity);
    }

    [Fact]
    public void ReadsNamespacedReportAndImmediateChildProperties()
    {
        const string report = """
            <r:UpdateReport xmlns:r="urn:dell:test">
              <r:Update>
                <r:UpdateID>driver1</r:UpdateID>
                <r:Name>Intel WiFi</r:Name>
                <r:UpdateType>driver</r:UpdateType>
                <r:ReleaseVersion>24.1.2</r:ReleaseVersion>
              </r:Update>
            </r:UpdateReport>
            """;
        var candidate = Assert.Single(DellApplicableUpdateParser.Parse(report));
        Assert.Equal("driver1", candidate.Id);
        Assert.Equal("24.1.2", candidate.Version);
        Assert.Equal("N/D", candidate.Severity);
    }

    [Fact]
    public void EmptyValidUpdateReportHasNoResults()
    {
        Assert.Empty(DellApplicableUpdateParser.Parse("<Updates/>"));
    }

    [Theory]
    [InlineData("<Unrelated><Update id=\"x\" name=\"fake\" type=\"Driver\" /></Unrelated>")]
    [InlineData("<Updates><Update id=\"x\" name=\"fake\" /></Updates>")]
    [InlineData("<Updates><Update type=\"Driver\" name=\"missing-id\" /></Updates>")]
    [InlineData("<Updates><Update type=\"Driver\" id=\"x\" /></Updates>")]
    [InlineData("<!DOCTYPE Updates [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><Updates>&x;</Updates>")]
    [InlineData("<Updates><Update></Updates>")]
    [InlineData("")]
    public void UntrustedOrIncompleteXmlIsNeverConsideredEmptySuccess(string report) =>
        Assert.ThrowsAny<Exception>(() => DellApplicableUpdateParser.Parse(report));

    [Fact]
    public void RejectsTooLargeReport() =>
        Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse(new string('x', 1024 * 1024 + 1)));

    [Fact]
    public void RejectsMoreThan150UpdateNodes()
    {
        var updates = string.Concat(Enumerable.Range(0, 151).Select(i =>
            $"<Update id=\"{i}\" name=\"Example\" type=\"Driver\" />"));
        Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse("<Updates>" + updates + "</Updates>"));
    }

    [Fact]
    public void DellReportStaysInInformationalOemCollection()
    {
        var package = new DellApplicableUpdateParser.Package(
            "abc", "NVIDIA Laptop GPU", "32.0", "Recommended");
        var row = Assert.Single(DellApplicableUpdateParser.ToVendorChecks([package]));
        Assert.True(row.IsCatalogMetadata);
        Assert.True(row.IsVendorApplicable);
        Assert.Equal("Dell", row.Vendor);
        Assert.Equal(0, row.WuaMatches);
        Assert.Equal("32.0", row.PublishedVersion);
        Assert.Contains("non installabile", row.StatusText);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(row.OfficialUrl));
    }
    [Fact]
    public void FirmwareAndSoftwareFloodDoesNotHideLateNvidiaAndKillerDrivers()
    {
        var unrelated = string.Concat(Enumerable.Range(0, 210)
            .Select(i => $"""<Update id="bios{i}" name="BIOS {i}" type="{(i % 2 == 0 ? "BIOS" : "Application")}" />"""));
        var report = "<Updates>" + unrelated +
            """
              <Update id="gpu-rtx" name="NVIDIA RTX 4060" type="Driver"
                      version="32.0.16.2101" />
              <Update id="wifi-ax1675" name="Killer AX1675i" type="Driver"
                      version="24.80.0.2" />
              <Update id="fw-late" name="System Firmware" type="Firmware" />
              </Updates>
              """;

        var result = DellApplicableUpdateParser.Parse(report);
        Assert.Equal(2, result.Count);
        Assert.Equal("gpu-rtx", result[0].Id);
        Assert.Equal("32.0.16.2101", result[0].Version);
        Assert.Equal("wifi-ax1675", result[1].Id);
        Assert.Equal("24.80.0.2", result[1].Version);
        Assert.All(DellApplicableUpdateParser.ToVendorChecks(result),
            row => Assert.False(row.IsInstallable));
    }

    [Fact]
    public void Exactly150UniqueDriversPlusDuplicatesAndOtherTypesAreAccepted()
    {
        var distinct = string.Concat(Enumerable.Range(0, 150)
            .Select(i => $"""<Update id="driver{i}" name="Driver {i}" type="Driver" />"""));
        var unrelated = string.Concat(Enumerable.Range(0, 205)
            .Select(i => $"""<Update id="firmware{i}" name="Firmware" type="Firmware" />"""));
        var duplicate = """<Update id="driver0" name="Duplicate" type="Driver" />""";
        var result = DellApplicableUpdateParser.Parse(
            "<Updates>" + unrelated + distinct + duplicate + "</Updates>");

        Assert.Equal(150, result.Count);
        Assert.Equal("driver0", result[0].Id);
        Assert.Equal("driver149", result[^1].Id);
        Assert.Single(result.Where(package => package.Id == "driver0"));
    }

    [Fact]
    public void Over150DistinctDriversStillFailsAfterSkippingOtherTypes()
    {
        var prefix = string.Concat(Enumerable.Range(0, 200)
            .Select(i => $"""<Update id="app{i}" name="App" type="Application" />"""));
        var drivers = string.Concat(Enumerable.Range(0, 151)
            .Select(i => $"""<Update id="driver{i}" name="Driver" type="Driver" />"""));

        var error = Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse(
                "<Updates>" + prefix + drivers + "</Updates>"));
        Assert.Contains("too many driver entries", error.Message);
    }

    [Fact]
    public void MissingTypeAfterMoreThan150NonDriverNodesStillFailsClosed()
    {
        var firmware = string.Concat(Enumerable.Range(0, 190)
            .Select(i => $"""<Update id="fw{i}" name="Firmware" type="Firmware" />"""));
        var report = "<Updates>" + firmware +
            """<Update id="gpu" name="GPU" type="Driver" />""" +
            """<Update id="suspicious" name="Unknown" />""" +
            "</Updates>";

        var error = Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse(report));
        Assert.Contains("type missing", error.Message);
    }

}
