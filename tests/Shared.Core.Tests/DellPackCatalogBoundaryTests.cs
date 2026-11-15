using System.Text;
using System.Xml.Linq;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #460: Dell Driver Pack metadata is bounded deployment evidence, not
/// a per-device Driver update, a verified signature or install permission.
/// Public Pack records cannot bypass XML validation at the UI boundary.
/// </summary>
public sealed class DellPackCatalogBoundaryTests
{
    private const string Model = "Latitude 7440";
    private const string Path = "FOLDER/latitude_7440_A01.cab";

    private static XElement Package(string path = Path,
        string model = Model, string release = "A01",
        string os = "WIN11", string type = "System") =>
        new("DriverPackage",
            new XAttribute("path", path),
            new XAttribute("dellVersion", release),
            new XAttribute("type", type),
            new XElement("SupportedSystems",
                new XElement("Brand", new XElement("Model",
                    new XAttribute("name", model)))),
            new XElement("SupportedOperatingSystems",
                new XElement("OperatingSystem",
                    new XAttribute("osCode", os))));

    private static MemoryStream Xml(params XElement[] packages) =>
        new(Encoding.UTF8.GetBytes(
            new XElement("DriverPackManifest", packages)
                .ToString(SaveOptions.DisableFormatting)));

    private static DellDriverPackCatalog.Pack Direct(string path = Path,
        string model = Model, string release = "A01",
        string os = "WIN11") =>
        new(model, path, release, os);

    [Fact]
    public void NormalDellModelDeploymentAndWindowsListRemainVisible()
    {
        using var xml = Xml(Package(), Package(
            "FOLDER/winpe.cab", type: "WinPE"));
        var pack = Assert.Single(DellDriverPackCatalog.FindForModel(
            xml, " latitude 7440 "));

        Assert.Equal(Path, pack.PackagePath);
        Assert.Equal("latitude 7440", pack.Model); // Preserve caller casing
        Assert.Equal("A01", pack.Release);
        Assert.Equal("WIN11", pack.OperatingSystems);
        var result = Assert.Single(
            DiscoveryResultAdapter.FromDell([pack]));
        Assert.Equal(DiscoveryResultKind.DellDeploymentPack, result.Kind);
        Assert.Equal("Solo catalogo · non installabile",
            result.VerificationLabel);
        Assert.Contains("WIN11", result.Evidence);
        Assert.Equal(OemPackageAssessment.Kind.DeploymentPack,
            OemPackageAssessment.FromDell(pack).PackageKind);
        Assert.False(OemPackageAssessment.FromDell(pack).IsInstallable);
    }

    [Theory]
    [InlineData("../driver.cab")]
    [InlineData("FOLDER/../../driver.cab")]
    [InlineData("FOLDER//driver.cab")]
    [InlineData("FOLDER/./driver.cab")]
    [InlineData("FOLDER/driver.cab/")]
    [InlineData("/FOLDER/driver.cab")]
    [InlineData(@"FOLDER\driver.cab")]
    [InlineData("C:/driver.cab")]
    [InlineData("https://external.invalid/driver.cab")]
    [InlineData("FOLDER/driver.cab?instance=123")]
    [InlineData("FOLDER/driver.cab#fragment")]
    [InlineData("FOLDER/name@host.cab")]
    [InlineData("FOLDER/driver file.cab")]
    [InlineData("FOLDER/name\u202E.cab")]
    [InlineData("FOLDER/name\n.cab")]
    [InlineData("FOLDER/%2e%2e/driver.cab")]
    public void UnsafeRelativePathsCannotBecomeTrustedDeploymentIdentity(
        string invalid)
    {
        using var xml = Xml(Package(invalid));
        Assert.Empty(DellDriverPackCatalog.FindForModel(xml, Model));

        Assert.Throws<InvalidDataException>(() =>
            DiscoveryResultAdapter.FromDell([Direct(path: invalid)]));
        Assert.Throws<InvalidDataException>(() =>
            OemPackageAssessment.FromDell(Direct(path: invalid)));
    }

    [Fact]
    public void OverlongPackagePathIsRejectedWithoutTruncatingIdentity()
    {
        var longPath = "FOLDER/" + new string('A', 510);
        using var xml = Xml(Package(longPath));
        Assert.Empty(DellDriverPackCatalog.FindForModel(xml, Model));

        Assert.Throws<InvalidDataException>(() =>
            DiscoveryResultAdapter.FromDell([Direct(path: longPath)]));
        Assert.Throws<InvalidDataException>(() =>
            OemPackageAssessment.FromDell(Direct(path: longPath)));
    }

    [Theory]
    [InlineData(@"C:\Users\PRIVATE_USER\driver.inf")]
    [InlineData("C:/Users/PRIVATE_USER/driver.inf")]
    [InlineData("/home/local-user/secret")]
    [InlineData("https://local.invalid/?serial=PRIVATE_SERIAL")]
    [InlineData("owner@example.invalid")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0")]
    [InlineData("SUBSYS_16A217AA")]
    [InlineData("SERIALNUMBER10001")]
    public void XmlDisplayMetadataPrivateTailIsRedactedBeforeAnyTruncation(
        string secret)
    {
        // A real hardware model is bounded to 160 chars by the
        // consumer. Keep its test value within that limit while placing
        // the private marker past the metadata preview on OS and release.
        var modelWithPrivateText = new string('Q', 100) + secret;
        var longText = new string('Q', 180) + secret;
        using var xml = Xml(Package(
            model: modelWithPrivateText, release: longText, os: longText));
        var pack = Assert.Single(DellDriverPackCatalog.FindForModel(
            xml, modelWithPrivateText));

        Assert.Equal("[dato riservato]", pack.Model);
        Assert.Equal("[dato riservato]", pack.Release);
        Assert.Equal("[dato riservato]", pack.OperatingSystems);
        var view = Assert.Single(
            DiscoveryResultAdapter.FromDell([pack]));
        Assert.DoesNotContain(secret, view.Title, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, view.Version,
            StringComparison.Ordinal);
        Assert.DoesNotContain(secret, view.Evidence,
            StringComparison.Ordinal);
        Assert.Equal("Solo catalogo · non installabile",
            view.VerificationLabel);
    }

    [Theory]
    [InlineData(@"C:\Users\PRIVATE_USER\driver.inf")]
    [InlineData("owner@example.invalid")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    public void PublicPackMetadataCannotBypassDiscoveryRedaction(
        string secret)
    {
        var tail = new string('Q', 170) + secret;
        var pack = Direct(model: tail, release: tail, os: tail);
        var discovery = Assert.Single(
            DiscoveryResultAdapter.FromDell([pack]));
        Assert.Equal("Driver Pack per [dato riservato]",
            discovery.Title);
        Assert.Equal("[dato riservato]", discovery.Version);
        Assert.Contains("[dato riservato]", discovery.Evidence);
        Assert.DoesNotContain(secret, discovery.Evidence,
            StringComparison.Ordinal);
        Assert.Equal(Path, OemPackageAssessment.FromDell(pack).PackageId);
        Assert.False(OemPackageAssessment.FromDell(pack).IsInstallable);
    }

    [Fact]
    public void UnverifiablyLongOsListIsReportedAsUnknownRatherThanShortened()
    {
        var manyOs = Enumerable.Range(0, 70).Select(i =>
            new XElement("OperatingSystem",
                new XAttribute("osCode", $"WIN{i:D2}")));
        var package = Package();
        package.Element("SupportedOperatingSystems")!
            .ReplaceNodes(manyOs);
        using var xml = Xml(package);

        var found = Assert.Single(DellDriverPackCatalog.FindForModel(
            xml, Model));
        Assert.Contains("verificare", found.OperatingSystems);
        Assert.DoesNotContain("WIN69", found.OperatingSystems);
    }

    [Fact]
    public void DuplicatePathsDoNotUseUpDistinctPackBudget()
    {
        using var xml = Xml(Enumerable.Range(0, 130)
            .Select(_ => Package()).ToArray());
        var one = Assert.Single(DellDriverPackCatalog.FindForModel(
            xml, Model));
        Assert.Equal(Path, one.PackagePath);
    }

    [Fact]
    public void MoreThanOneHundredDistinctPacksAreExplicitlyUnverified()
    {
        using var xml = Xml(Enumerable.Range(0,
            DellDriverPackCatalog.MaximumMatchedPacks + 1)
            .Select(i => Package($"FOLDER/pack_{i:D3}.cab"))
            .ToArray());
        Assert.Throws<InvalidDataException>(() =>
            DellDriverPackCatalog.FindForModel(xml, Model));
    }

    [Fact]
    public void ExactlyOneHundredGenuinePacksRemainVisible()
    {
        using var xml = Xml(Enumerable.Range(0,
            DellDriverPackCatalog.MaximumMatchedPacks)
            .Select(i => Package($"FOLDER/pack_{i:D3}.cab"))
            .ToArray());
        var packs = DellDriverPackCatalog.FindForModel(xml, Model);
        Assert.Equal(DellDriverPackCatalog.MaximumMatchedPacks,
            packs.Count);
    }
}
