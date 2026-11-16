using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #510: DCU direct attributes, child elements and schema aliases are one
/// field each. Conflicts invalidate the scan, not a per-PC driver offer.
/// </summary>
public sealed class DellDuplicateXmlMetadataBoundaryTests
{
    private static string Report(string update) =>
        "<Updates>" + update + "</Updates>";

    [Theory]
    [InlineData("""<Update id="gpu" name="GPU" type="Driver"><UpdateType>Firmware</UpdateType></Update>""")]
    [InlineData("""<Update id="gpu" name="GPU" type="Firmware"><UpdateType>Driver</UpdateType></Update>""")]
    [InlineData("""<Update id="gpu" name="GPU" type="Driver" UpdateType="Driver" />""")]
    [InlineData("""<Update id="gpu" name="GPU" type="Driver" TYPE="Driver" />""")]
    [InlineData("""<Update id="gpu" name="GPU" type="" UpdateType="Driver" />""")]
    [InlineData("""<Update id="gpu" name="GPU"><Type>Firmware</Type><UpdateType>Driver</UpdateType></Update>""")]
    [InlineData("""<Update id="gpu" name="GPU" type="Driver"><Type /></Update>""")]
    [InlineData("""<Update id="gpu" name="GPU" type="Driver"><x:UpdateType xmlns:x="urn:other">Firmware</x:UpdateType></Update>""")]
    public void TypeAndUpdateTypeDuplicatesCannotFabricateDrivers(
        string update)
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse(Report(update)));
        Assert.Contains("ambiguous update metadata", error.Message);
        Assert.DoesNotContain("Firmware", error.Message);
        Assert.DoesNotContain("urn:other", error.Message);
    }

    [Theory]
    [InlineData("""<Update type="Driver" id="gpu" ComponentID="other" name="GPU" />""")]
    [InlineData("""<Update type="Driver" id="gpu" ID="gpu" name="GPU" />""")]
    [InlineData("""<Update type="Driver" id="" name="GPU"><UpdateID>gpu</UpdateID></Update>""")]
    [InlineData("""<Update type="Driver" id="gpu" name="GPU"><PackageID>other</PackageID></Update>""")]
    [InlineData("""<Update type="Driver" id="gpu" name="GPU" title="Other" />""")]
    [InlineData("""<Update type="Driver" id="gpu" name="GPU"><Title>Second name</Title></Update>""")]
    [InlineData("""<Update type="Driver" id="gpu" name="GPU" Version="1" ReleaseVersion="2" />""")]
    [InlineData("""<Update type="Driver" id="gpu" name="GPU" Severity="Recommended"><UpdateSeverity>Critical</UpdateSeverity></Update>""")]
    [InlineData("""<Update type="Driver" id="gpu" name="GPU" Severity="" UpdateSeverity="Critical" />""")]
    public void IdentityDisplayAndVersionAliasesCannotBeOverridden(
        string update)
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse(Report(update)));
        Assert.Contains("ambiguous update metadata", error.Message);
        Assert.DoesNotContain("other", error.Message);
        Assert.DoesNotContain("Critical", error.Message);
    }

    [Fact]
    public void AmbiguousSecondDriverCannotTurnPartialScanIntoSuccess()
    {
        const string report = """
            <Updates>
              <Update id="valid-gpu" name="NVIDIA RTX" type="Driver" />
              <Update id="private" name="C:\Users\SECRET\report.xml"
                      type="Driver">
                <UpdateType>Firmware</UpdateType>
              </Update>
            </Updates>
            """;
        var error = Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse(report));
        Assert.DoesNotContain("SECRET", error.Message);
        Assert.DoesNotContain("private", error.Message);
        Assert.DoesNotContain("NVIDIA", error.Message);
    }

    [Fact]
    public void AmbiguousFirmwareNodeCannotHideBehindDriverOrZeroResults()
    {
        const string report = """
            <Updates>
              <Update id="bios" name="BIOS" type="Firmware">
                <UpdateType>Driver</UpdateType>
              </Update>
              <Update id="gpu" name="GPU" type="Driver" />
            </Updates>
            """;
        Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse(report));
    }

    [Fact]
    public void TwoNamespacedAliasesAreAmbiguousEvenWithSameValue()
    {
        const string report = """
            <r:UpdateReport xmlns:r="urn:dell:test" xmlns:s="urn:other">
              <r:Update r:UpdateID="gpu" r:Name="NVIDIA" r:Type="Driver">
                <s:UpdateType>Driver</s:UpdateType>
              </r:Update>
            </r:UpdateReport>
            """;
        Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse(report));
    }

    [Theory]
    [InlineData("""<Update id="gpu" name="GPU" type="Driver"><Name><Nested>Private</Nested></Name></Update>""")]
    [InlineData("""<Update id="gpu" name="GPU" type="Driver"><Version><Nested>1</Nested></Version></Update>""")]
    [InlineData("""<Update id="gpu" name="GPU"><Type><Nested>Driver</Nested></Type></Update>""")]
    public void NestedFieldsRemainInvalid(string update)
    {
        Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse(Report(update)));
    }

    [Fact]
    public void SingleAttributeOrChildAliasesRemainValidOnDcuXml()
    {
        const string report = """
            <r:UpdateReport xmlns:r="urn:dell:test">
              <r:Update r:ComponentID="gpu"
                        r:Title="NVIDIA RTX 4060" r:Type="Driver">
                <r:ReleaseVersion>32.0.16.2101</r:ReleaseVersion>
                <r:UpdateSeverity>Recommended</r:UpdateSeverity>
              </r:Update>
              <r:Update>
                <r:PackageID>killer</r:PackageID>
                <r:Name>Intel Killer</r:Name>
                <r:UpdateType>Driver</r:UpdateType>
                <r:Version>24.8.0.1</r:Version>
              </r:Update>
              <r:Update r:UpdateID="firmware"
                        r:Name="Dock" r:UpdateType="Firmware" />
            </r:UpdateReport>
            """;
        var result = DellApplicableUpdateParser.Parse(report);
        Assert.Equal(2, result.Count);
        Assert.Equal("gpu", result[0].Id);
        Assert.Equal("NVIDIA RTX 4060", result[0].Title);
        Assert.Equal("32.0.16.2101", result[0].Version);
        Assert.Equal("Recommended", result[0].Severity);
        Assert.Equal("killer", result[1].Id);
        Assert.Equal("Intel Killer", result[1].Title);
        Assert.Equal("24.8.0.1", result[1].Version);
        Assert.Equal("N/D", result[1].Severity);
        Assert.All(DellApplicableUpdateParser.ToVendorChecks(result),
            row => Assert.False(row.IsInstallable));
    }

    [Fact]
    public void SingleEmptyOptionalFieldStillHasUnavailableDefault()
    {
        const string report = """
            <Updates>
              <Update id="gpu" name="NVIDIA RTX" type="Driver"
                      version="" severity=" " />
            </Updates>
            """;
        var row = Assert.Single(DellApplicableUpdateParser.Parse(report));
        Assert.Equal("N/D", row.Version);
        Assert.Equal("N/D", row.Severity);
    }
}
