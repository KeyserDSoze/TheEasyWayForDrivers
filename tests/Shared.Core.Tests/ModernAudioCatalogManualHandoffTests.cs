using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Browser-only Microsoft Update Catalog handoff for verified public
/// SNDW and ACP device models. Neither the link nor a WUA match establishes
/// a downloadable/applicable/newer driver or installation permission.
/// </summary>
public sealed class ModernAudioCatalogManualHandoffTests
{
    private const string Sndw =
        @"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00";
    private const string SndwPublic =
        @"SNDW\CLASS_00&MAN_025D&PART_0711";
    private const string Acp =
        @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2";
    private const string GuidOne =
        "00000000-0000-4000-8000-000000000357";

    private static DriverInfo Device(
        IReadOnlyList<string> hardware,
        IReadOnlyList<string>? compatible = null) =>
        new(@"SNDW\PRIVATE_DEVICE_INSTANCE_SERIAL",
            "PRIVATE_NOTEBOOK_MODEL",
            "Microsoft", "MEDIA", "Microsoft", "1.0",
            null, "PRIVATE_OEM.inf", true, true, 0,
            hardware, compatible ?? []);

    private static DriverUpdateInfo Wua(string id) =>
        new(GuidOne, "Windows audio", null, "MEDIA", "Microsoft",
            null, null, false, "Microsoft", null,
            id, null, SearchSource: "Windows Update online");

    private static string Inf(string id) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\n%Vendor%=Models,NTamd64\n" +
        "[Models.NTamd64]\n%Audio%=Install," + id + "\n";

    [Theory]
    [InlineData(Sndw, SndwPublic)]
    [InlineData(SndwPublic, SndwPublic)]
    [InlineData(Acp, Acp)]
    [InlineData(@"sndw\class_00&man_025d&part_0711&ver_02&uid_00",
        SndwPublic)]
    [InlineData(@"acp\devtype_0005&devrev_0000&ven_1022&dev_15e2",
        Acp)]
    public void StrictModelsYieldPublicConsentOnlyCatalogQuery(
        string model, string expected)
    {
        Assert.True(ModernAudioPnpMatchPolicy.IsSpecificHardwareId(model));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(model));

        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device([model])));

        Assert.Equal(expected, target.Query);
        Assert.Equal(expected,
            MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(model));
        Assert.False(target.FromCompatibleId);
        Assert.False(target.CatalogWasQueried);
        Assert.False(target.IsInstallable);
        Assert.Equal(Uri.UriSchemeHttps, target.Url.Scheme);
        Assert.Equal(MicrosoftUpdateCatalogSearch.CatalogHost, target.Url.Host);
        Assert.Equal("/Search.aspx", target.Url.AbsolutePath);
        Assert.Equal("q=" + expected,
            Uri.UnescapeDataString(target.Url.Query.TrimStart('?')));
        Assert.Contains("firma CAT", target.Explanation);
        Assert.Contains("modello hardware", target.Explanation);
        Assert.DoesNotContain("aggiornamento disponibile", target.Explanation);
        Assert.DoesNotContain("PRIVATE", target.Url.AbsoluteUri);
        Assert.DoesNotContain("SERIAL", target.Url.AbsoluteUri);
        Assert.DoesNotContain("PRIVATE", target.Query);
        Assert.DoesNotContain("PRIVATE_NOTEBOOK_MODEL",
            target.Url.AbsoluteUri);
    }

    [Fact]
    public void FullSndwQueryNeverTransmitsVariantUidOrPrivatePnpInstance()
    {
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device([Sndw])));
        Assert.Equal(SndwPublic, target.Query);
        Assert.DoesNotContain("VER_", target.Query);
        Assert.DoesNotContain("UID_", target.Query);
        Assert.DoesNotContain("PRIVATE", target.Url.AbsoluteUri);
        Assert.DoesNotContain("PRIVATE_OEM", target.Url.AbsoluteUri);
        Assert.DoesNotContain("SNDW%5CPRIVATE",
            target.Url.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%5C", target.Url.AbsoluteUri);
        Assert.Contains("%26", target.Url.AbsoluteUri);
    }

    [Fact]
    public void AcpQueryPreservesOnlyFixedWidthPublicModelNotInstance()
    {
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device([Acp])));

        Assert.Equal(Acp, target.Query);
        Assert.Contains(@"VEN_1022&DEV_15E2", target.Query);
        Assert.DoesNotContain("SNDW", target.Query);
        Assert.DoesNotContain("PRIVATE", target.Url.AbsoluteUri);
        Assert.False(target.CatalogWasQueried);
        Assert.False(target.IsInstallable);
    }

    [Fact]
    public void HardwareIdPreferredAndCompatibleFallbackIsExplicitlyWeaker()
    {
        var preferred = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device(
                [Sndw], [Acp])));
        Assert.Equal(SndwPublic, preferred.Query);
        Assert.False(preferred.FromCompatibleId);

        var fallback = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device(
                [@"ROOT\MEDIA"], [Acp])));
        Assert.Equal(Acp, fallback.Query);
        Assert.True(fallback.FromCompatibleId);
        Assert.Contains("evidenza più debole", fallback.Explanation);

        var genericFirst = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device(
                [@"SNDW\CLASS_00", Sndw])));
        Assert.Equal(SndwPublic, genericFirst.Query);
        Assert.False(genericFirst.FromCompatibleId);
    }

    [Theory]
    [InlineData(@"SNDW\CLASS_00")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_071")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&UID_00")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&FAKE_01")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711\PRIVATE_SERIAL")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00" + "\n")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00" + "\u202e")]
    [InlineData(@"ACP\DEVTYPE_0005")]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000")]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022")]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E")]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2&REV_01")]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2\PRIVATE_SERIAL")]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2/evil")]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2%26")]
    [InlineData(@"C:\PRIVATE_ORDER\codec.inf")]
    public void IncompleteMalformedAndPrivateModelsCannotBuildQuery(
        string model)
    {
        Assert.False(ModernAudioPnpMatchPolicy.IsSpecificHardwareId(model));
        Assert.Null(MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(model));
        Assert.Null(MicrosoftUpdateCatalogSearch.Create(Device([model])));
    }

    [Fact]
    public void NoHardwareIdMeansNoCatalogLinkDespiteMisleadingDeviceName()
    {
        Assert.Null(MicrosoftUpdateCatalogSearch.Create(Device([])));
        Assert.Null(MicrosoftUpdateCatalogSearch.Create(Device(
            [@"ROOT\MEDIA"], [@"ACP\DEVTYPE_0005"])));
    }

    [Theory]
    [InlineData(Sndw)]
    [InlineData(Acp)]
    public void ManualCatalogQueryDoesNotCreateNewWindowsUpdateOffer(
        string hardwareId)
    {
        var driver = Device([hardwareId]);
        var query = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(driver));
        Assert.False(query.CatalogWasQueried);
        Assert.False(query.IsInstallable);

        var noWuaResult = DeviceWuaMatchGroups.Create(driver, []);
        Assert.Empty(noWuaResult.Ordinary);
        Assert.Empty(noWuaResult.ReviewOnly);
        Assert.Empty(noWuaResult.ModelOnlyReview ?? []);

        // A genuine WUA GUID, if independently confirmed, still uses
        // exact hardware matching (NOT the broader browser search query).
        var verified = Wua(hardwareId);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(driver, verified));
        Assert.Equal(2, InfModelEvidence.MatchStrength(driver,
            InfModelEvidence.ReadDeclaredIds(Inf(hardwareId))));
        Assert.Single(DeviceWuaMatchGroups.Create(
            driver, [verified]).Ordinary);
        Assert.False(QuickUpdatePolicy.IsEligible(
            verified with { IsAdvancedCandidate = true }));
    }

    [Fact]
    public void OneGenericModelCannotBePromotedByOtherBusCompatibleId()
    {
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device(
                [@"ACP\DEVTYPE_0005"], [Sndw])));

        Assert.Equal(SndwPublic, target.Query);
        Assert.True(target.FromCompatibleId);
        Assert.False(target.IsInstallable);
    }
}
