using System.Text.Json;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #540: CMSL must not expose the current PC's customer/order references
/// through a seemingly public SoftPaq title, version or opaque identity.
/// Results are vendor metadata only, not applicable/installable drivers.
/// </summary>
public sealed class HpCmslCustomerMarkerPrivacyTests
{
    private static string Json(string id = "sp54001",
        string title = "NVIDIA graphics", string version = "31.0",
        string releaseType = "Recommended", string date = "2026-11-17") =>
        JsonSerializer.Serialize(new
        {
            Id = id, Name = title, Version = version,
            ReleaseType = releaseType, ReleaseDate = date,
            Category = "Driver - Graphics"
        });

    [Theory]
    [InlineData("ORDER_987654")]
    [InlineData("CUSTOMER_31337")]
    [InlineData("SECRET_KEY123")]
    [InlineData("SERIAL_12345")]
    [InlineData("PRIVATE_MACHINE")]
    [InlineData("ACCOUNT_1111")]
    [InlineData("USERNAME_ALICE")]
    [InlineData("HOSTNAME_OFFICE")]
    [InlineData("PASSWORD_DUMMY")]
    [InlineData("EMAIL_USER")]
    [InlineData("DEVICEID_LOCAL")]
    [InlineData("INSTANCEID_LOCAL")]
    [InlineData("UUID_ABC")]
    [InlineData("CUS\u200BTOMER_31337")]
    [InlineData("OR\u2066DER_987654")]
    [InlineData("SE\u202CRET_KEY123")]
    public void AllCmslDisplayFieldsRedactPrivateMarkers(string marker)
    {
        var privateField = "HP device " + marker;
        var row = Assert.Single(HpSoftpaqCatalogParser.Parse(Json(
            title: privateField, version: privateField,
            releaseType: privateField, date: privateField)));

        Assert.Equal("sp54001", row.Id);
        Assert.Equal("[dato riservato]", row.Title);
        Assert.Equal("[dato riservato]", row.Version);
        Assert.Equal("[dato riservato]", row.ReleaseType);
        Assert.Equal("[dato riservato]", row.PublishedDate);
        var discovery = Assert.Single(DiscoveryResultAdapter.FromHp([row]));
        Assert.Equal("[dato riservato]", discovery.Title);
        Assert.Equal("[dato riservato]", discovery.Version);
        Assert.DoesNotContain(marker, discovery.Title + discovery.Evidence);
        Assert.Equal("Solo catalogo · non installabile",
            discovery.VerificationLabel);
    }

    [Fact]
    public void SensitiveMarkerAfterDisplayLimitDoesNotPassAsSafePrefix()
    {
        var value = new string('A', 175) + " CUSTOMER_ORDER9876";
        var row = Assert.Single(HpSoftpaqCatalogParser.Parse(Json(
            title: value, version: value, releaseType: value, date: value)));
        Assert.Equal("[dato riservato]", row.Title);
        Assert.Equal("[dato riservato]", row.Version);
        Assert.Equal("[dato riservato]", row.ReleaseType);
        Assert.Equal("[dato riservato]", row.PublishedDate);
        var discovery = Assert.Single(DiscoveryResultAdapter.FromHp([row]));
        Assert.DoesNotContain("ORDER9876", discovery.Title + discovery.Evidence);
    }

    [Theory]
    [InlineData("sp54001_CUSTOMER_31337")]
    [InlineData("sp54001_SECRET_KEY")]
    [InlineData("sp54001_SERIAL_12345")]
    [InlineData("sp54001_ORDER_987654")]
    [InlineData("sp54001_PRIVATE_MACHINE")]
    [InlineData("sp54001_UUID_ABC")]
    public void PrivateSoftpaqIdentityRejectedInJsonAndDirectEntry(string id)
    {
        Assert.Empty(HpSoftpaqCatalogParser.Parse(Json(id: id)));
        var entry = new HpSoftpaqEntry(id, "NVIDIA graphics", "31.0",
            "Recommended", "2026-11-17");
        Assert.Throws<InvalidDataException>(() =>
            HpSoftpaqCatalogParser.ValidateEntry(entry));
        Assert.Throws<InvalidDataException>(() =>
            DiscoveryResultAdapter.FromHp([entry]));
    }

    [Fact]
    public void RejectedPrivateIdDoesNotPreemptValidLaterSoftpaq()
    {
        var json = "[" +
            Json("sp54001_CUSTOMER_31337", "Customer-specific GPU") +
            "," + Json("sp54001", "NVIDIA public GPU") +
            "," + Json("sp54002", "Intel Killer Wi-Fi") +
            "]";
        var rows = HpSoftpaqCatalogParser.Parse(json);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.Id == "sp54001" &&
            x.Title == "NVIDIA public GPU");
        Assert.Contains(rows, x => x.Id == "sp54002" &&
            x.Title == "Intel Killer Wi-Fi");
    }

    [Fact]
    public void PublicSoftpaqAndInternationalTextRetainSameReadOnlySemantics()
    {
        var json = "[" +
            Json("sp54001", "Gráfica àudio", "31.0") +
            "," + Json("sp54002", "Intel Killer Wi-Fi", "23.2") +
            "]";
        var parsed = HpSoftpaqCatalogParser.Parse(json);
        Assert.Equal(2, parsed.Count);
        Assert.Contains(parsed, row => row.Title == "Gráfica àudio");
        Assert.Contains(parsed, row => row.Id == "sp54002");
        Assert.All(DiscoveryResultAdapter.FromHp(parsed), result =>
        {
            Assert.Equal(DiscoveryResultKind.HpSoftpaqDriver, result.Kind);
            Assert.Equal("Solo catalogo · non installabile",
                result.VerificationLabel);
            Assert.Null(result.HardwareId);
        });

        var direct = new HpSoftpaqEntry("sp54003",
            "Realtek audio ORDER_5522", "3.0 SECRET_X",
            "Recommended CUSTOMER_987", "2026-11-17");
        var legacy = HpSoftpaqCatalogParser.ValidateEntry(direct);
        Assert.Equal("[dato riservato]", legacy.Title);
        Assert.Equal("[dato riservato]", legacy.Version);
        Assert.Equal("[dato riservato]", legacy.ReleaseType);
        Assert.Equal("2026-11-17", legacy.PublishedDate);
        var display = Assert.Single(DiscoveryResultAdapter.FromHp([direct]));
        Assert.Equal("[dato riservato]", display.Title);
        Assert.DoesNotContain("ORDER_5522", display.Title + display.Evidence);
    }
}
