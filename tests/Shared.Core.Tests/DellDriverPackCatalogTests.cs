using System.Text;
using System.Xml;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DellDriverPackCatalogTests
{
    private const string Sample = """
        <DriverPackManifest baseLocation="downloads.dell.com">
          <DriverPackage type="System" path="FOLDER/latitude.exe" dellVersion="A10">
            <SupportedSystems><Brand><Model name="Latitude 7440" /></Brand></SupportedSystems>
            <SupportedOperatingSystems><OperatingSystem osCode="WIN11" osArch="x64" /></SupportedOperatingSystems>
          </DriverPackage>
          <DriverPackage type="System" path="FOLDER/optiplex.exe" dellVersion="A03">
            <SupportedSystems><Brand><Model name="OptiPlex 7090" /></Brand></SupportedSystems>
          </DriverPackage>
          <DriverPackage type="WinPE" path="FOLDER/winpe.exe" dellVersion="A01">
            <SupportedSystems><Brand><Model name="Latitude 7440" /></Brand></SupportedSystems>
          </DriverPackage>
        </DriverPackManifest>
        """;

    private static MemoryStream Xml(string text) =>
        new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void FindForModel_FindsOnlyExactModelAndExcludesWinPe()
    {
        using var xml = Xml(Sample);
        var pack = Assert.Single(DellDriverPackCatalog.FindForModel(xml, " latitude 7440 "));
        Assert.Equal("FOLDER/latitude.exe", pack.PackagePath);
        Assert.Equal("A10", pack.Release);
        Assert.Contains("WIN11", pack.OperatingSystems);
    }

    [Fact]
    public void FindForModel_DoesNotInferSimilarModelCompatibility()
    {
        using var xml = Xml(Sample);
        Assert.Empty(DellDriverPackCatalog.FindForModel(xml, "Latitude 744"));
    }

    [Fact]
    public void FindForModel_RejectsDtdAndExternalEntities()
    {
        using var xml = Xml("<!DOCTYPE DriverPackManifest [<!ENTITY ex SYSTEM 'file:///c:/windows/win.ini'>]><DriverPackManifest>&ex;</DriverPackManifest>");
        Assert.Throws<XmlException>(() =>
            DellDriverPackCatalog.FindForModel(xml, "Latitude 7440"));
    }

    [Fact]
    public void FindForModel_DiscardsUnsafePaths()
    {
        using var xml = Xml(Sample.Replace("FOLDER/latitude.exe", "../untrusted.exe"));
        Assert.Empty(DellDriverPackCatalog.FindForModel(xml, "Latitude 7440"));
    }

    [Fact]
    public void FindForModel_NoModelReturnsNoResults()
    {
        using var xml = Xml(Sample);
        Assert.Empty(DellDriverPackCatalog.FindForModel(xml, null));
    }
}
