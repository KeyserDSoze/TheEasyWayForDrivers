using System.Text.Json;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #552: Lenovo MachineType metadata is public catalog evidence, not
/// permission to export the PC user's identities or order/serial values.
/// </summary>
public sealed class LenovoCatalogPrivateMarkerBoundaryTests
{
    private static string Row(
        string id = "n3jw605w",
        string name = "Lenovo wireless driver",
        string version = "2.1",
        string category = "Network",
        string date = "2026-11-18") =>
        JsonSerializer.Serialize(new
        {
            ID = id, PackageType = "2", Name = name, Version = version,
            Category = category, ReleaseDate = date
        });

    [Theory]
    [InlineData("SERIAL_12345")]
    [InlineData("ORDER_98765")]
    [InlineData("CUSTOMER_ABC")]
    [InlineData("HOSTNAME_OFFICE")]
    [InlineData("USERNAME_PCUSER")]
    [InlineData("ACCOUNT_1234")]
    [InlineData("PASSWORD_TEST")]
    [InlineData("EMAIL_PERSON")]
    [InlineData("SECRET_TOKEN")]
    [InlineData("DEVICEID_123")]
    [InlineData("UUID_1234")]
    [InlineData("PCI_DEV_28A0")]
    [InlineData("PCI_VEN_10DE")]
    public void PrivateAsciiPackageIdsAreNeverCatalogIdentity(string id)
    {
        Assert.Empty(LenovoCatalogParser.Parse(Row(id: id)));
        var direct = new LenovoCatalogParser.Package(
            id, "Wi-Fi", "2.1", "Network", "2026");
        Assert.Throws<InvalidDataException>(() =>
            DiscoveryResultAdapter.FromLenovo([direct]));
        Assert.Throws<InvalidDataException>(() =>
            OemPackageAssessment.FromLenovo(direct));
    }

    [Theory]
    [InlineData("ORDER_4815")]
    [InlineData("CUSTOMER_77")]
    [InlineData("SERIAL_4060")]
    [InlineData("ACCOUNT_250")]
    [InlineData("HOSTNAME_PRIVATEPC")]
    [InlineData("PASSWORD_TEMP")]
    [InlineData("EMAIL_OWNER")]
    [InlineData("DEVICEID_USER")]
    [InlineData("VEN_10DE")]
    [InlineData("FUNC_01")]
    [InlineData("MI_00")]
    [InlineData("P\u200BRIVATE_51")]
    [InlineData("OR\u2060DER_4815")]
    [InlineData("CUST\u202EOMER_77")]
    public void EveryDisplayFieldRedactsPrivateAndInvisibleMarkers(
        string privateValue)
    {
        var contaminated = new string('A', 166) + privateValue;
        var parsed = Assert.Single(LenovoCatalogParser.Parse(
            Row(name: contaminated, version: contaminated,
                category: contaminated, date: contaminated)));
        Assert.Equal("[dato riservato]", parsed.Name);
        Assert.Equal("[dato riservato]", parsed.Version);
        Assert.Equal("[dato riservato]", parsed.Category);
        Assert.Equal("[dato riservato]", parsed.ReleaseDate);

        var discovery = Assert.Single(
            DiscoveryResultAdapter.FromLenovo([parsed]));
        Assert.Equal("n3jw605w", discovery.Identifier);
        Assert.Equal("[dato riservato]", discovery.Title);
        Assert.Equal("[dato riservato]", discovery.Version);
        Assert.DoesNotContain(privateValue, discovery.Evidence);
        Assert.Contains("[dato riservato]", discovery.Evidence);
    }

    [Fact]
    public void DirectPublicLenovoPackageUsesSamePrivacyGate()
    {
        var unsafeValue = "S\u200BERIAL_4724";
        var direct = new LenovoCatalogParser.Package(
            "Intel-Killer", unsafeValue, unsafeValue, unsafeValue, unsafeValue);

        var discovery = Assert.Single(
            DiscoveryResultAdapter.FromLenovo([direct]));
        Assert.Equal("Intel-Killer", discovery.Identifier);
        Assert.Equal("[dato riservato]", discovery.Title);
        Assert.Equal("[dato riservato]", discovery.Version);
        Assert.DoesNotContain("4724", discovery.Evidence);

        var assessment = OemPackageAssessment.FromLenovo(direct);
        Assert.Equal("Intel-Killer", assessment.PackageId);
        Assert.False(assessment.IsInstallable);
    }

    [Fact]
    public void RedactedDuplicateCannotHideLaterPublicSameLenovoPackage()
    {
        var records = new[]
        {
            Row("n3jw605w", "ORDER_PRIVATE_987", category: "Audio"),
            Row("n3jw605w", "Lenovo audio driver", category: "Audio"),
            Row("Intel-Killer", "Killer Wi-Fi", category: "Network")
        };
        var rows = LenovoCatalogParser.Parse(
            "[" + string.Join(",", records) + "]");
        Assert.Equal(2, rows.Count);
        Assert.Equal("Lenovo audio driver",
            rows.Single(row => row.Id == "n3jw605w").Name);
        Assert.Equal("Killer Wi-Fi",
            rows.Single(row => row.Id == "Intel-Killer").Name);
    }

    [Fact]
    public void ValidInternationalMetadataAndGenuinePackageIdsRemainVisible()
    {
        var source = Row("n3jw605w", "Réseau Wi-Fi Intel Killer",
            "3.0", "Réseau", "2026-11-18");
        var parsed = Assert.Single(LenovoCatalogParser.Parse(source));
        Assert.Equal("n3jw605w", parsed.Id);
        Assert.Equal("Réseau Wi-Fi Intel Killer", parsed.Name);
        Assert.Equal("Réseau", parsed.Category);
        Assert.Equal("3.0", parsed.Version);
        Assert.Equal("2026-11-18", parsed.ReleaseDate);
        Assert.Equal("NVIDIA-GPU", Assert.Single(
            LenovoCatalogParser.Parse(Row(id: "NVIDIA-GPU"))).Id);
    }
}
