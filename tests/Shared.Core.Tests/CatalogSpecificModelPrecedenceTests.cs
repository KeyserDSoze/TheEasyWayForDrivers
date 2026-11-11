using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Windows can enumerate a generic SoundWire aggregator before its
/// actual SDCA/SNDW/ACP codec model. A manual public Catalog lookup
/// should use the specific PnP model when it exists in the SAME list.
/// The link is not an installable driver candidate.
/// </summary>
public sealed class CatalogSpecificModelPrecedenceTests
{
    private const string Aggregate = @"SOUNDWIRE\AGGREGATOR";
    private const string Speaker = @"SOUNDWIRE\AGGREGATEDSPEAKER";
    private const string Sdca =
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711&TYPE_04&VER_01&ADR_01&LID_00&UID_00&SUBSYS_305610EC";
    private const string SdcaPublic =
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711";
    private const string Sndw =
        @"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00";
    private const string SndwPublic =
        @"SNDW\CLASS_00&MAN_025D&PART_0711";
    private const string Acp =
        @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2";
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";

    private static DriverInfo Device(
        IReadOnlyList<string> hardware,
        IReadOnlyList<string>? compatible = null) =>
        new(@"SOUNDWIRE\PRIVATE_SERIAL_INSTANCE",
            "PRIVATE_NOTEBOOK_NAME", "Microsoft", "MEDIA",
            "Microsoft", "1.0", null, "PRIVATE_DRIVER.inf",
            true, true, 0, hardware, compatible ?? []);

    [Theory]
    [InlineData(Sdca, SdcaPublic)]
    [InlineData(Sndw, SndwPublic)]
    [InlineData(Acp, Acp)]
    [InlineData(Nvidia, Nvidia)]
    public void GenericFirstHardwareIdCannotHideSpecificCodecOrGpu(
        string specific, string expectedQuery)
    {
        var device = Device([Aggregate, Speaker, specific]);
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(device));

        Assert.Equal(expectedQuery, target.Query);
        Assert.False(target.FromCompatibleId);
        Assert.False(target.IsInstallable);
        Assert.False(target.CatalogWasQueried);
        Assert.Equal(Uri.UriSchemeHttps, target.Url.Scheme);
        Assert.Equal(MicrosoftUpdateCatalogSearch.CatalogHost,
            target.Url.Host);
        Assert.DoesNotContain("PRIVATE", target.Url.AbsoluteUri);
        Assert.DoesNotContain("SERIAL", target.Url.AbsoluteUri);
        Assert.DoesNotContain("SUBSYS_", target.Url.AbsoluteUri);
        Assert.DoesNotContain("UID_", target.Url.AbsoluteUri);
        Assert.DoesNotContain("VER_", target.Url.AbsoluteUri);
        Assert.Contains("modello hardware", target.Explanation);
        Assert.DoesNotContain("categoria audio SoundWire generica",
            target.Explanation);

        // No update is synthesized by opening a browser search; WUA and
        // INF recognition continue to be independent of this link.
        Assert.Empty(DeviceWuaMatchGroups.Create(device, []).Ordinary);
    }

    [Fact]
    public void FirstSpecificHardwareModelWinsAmongOtherSpecificModels()
    {
        var result = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(
                Device([Speaker, Sdca, Acp, Sndw])));
        Assert.Equal(SdcaPublic, result.Query);
        Assert.False(result.FromCompatibleId);
    }

    [Theory]
    [InlineData(Aggregate)]
    [InlineData(Speaker)]
    public void GenericOnlyHardwareStillOffersClearlyLabeledManualSearch(
        string generic)
    {
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device([generic])));

        Assert.Equal(generic, target.Query);
        Assert.False(target.FromCompatibleId);
        Assert.Contains("categoria audio SoundWire generica",
            target.Explanation);
        Assert.False(target.CatalogWasQueried);
        Assert.False(target.IsInstallable);
    }

    [Fact]
    public void HardwareGenericStillOutranksMoreSpecificCompatibleModel()
    {
        // Do not silently promote a weaker compatible ID above a
        // Hardware ID just to generate a more-specific browser query.
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(
                Device([Aggregate], [Sndw, Acp])));

        Assert.Equal(Aggregate, target.Query);
        Assert.False(target.FromCompatibleId);
        Assert.Contains("categoria audio SoundWire generica",
            target.Explanation);
    }

    [Fact]
    public void SpecificCompatibleModelOutranksGenericCompatibleToken()
    {
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device(
                [@"ROOT\MEDIA"], [Aggregate, Speaker, Sndw])));

        Assert.Equal(SndwPublic, target.Query);
        Assert.True(target.FromCompatibleId);
        Assert.Contains("ID compatibile, evidenza più debole",
            target.Explanation);
        Assert.DoesNotContain("PRIVATE", target.Url.AbsoluteUri);
        Assert.False(target.IsInstallable);
    }

    [Fact]
    public void GenericCompatibleFallbackWorksWhenNoSpecificIdsExist()
    {
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(
                Device([@"ROOT\MEDIA"], [Speaker, Aggregate])));

        Assert.Equal(Speaker, target.Query);
        Assert.True(target.FromCompatibleId);
        Assert.Contains("categoria audio SoundWire generica",
            target.Explanation);
    }

    [Fact]
    public void MalformedOrPrivateIdsNeverTakePriorityOverSpecificHardware()
    {
        var device = Device(
        [
            @"SOUNDWIRE\AGGREGATOR&UID_00",
            @"SOUNDWIRE\SDCA_06&MAN_025D",
            @"ACP\DEVTYPE_0005",
            @"SNDW\CLASS_00&MAN_025D",
            @"SOUNDWIRE\AGGREGATEDSPEAKER",
            @"SOUNDWIRE\PRIVATE_SERIAL",
            Sdca
        ]);
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(device));
        Assert.Equal(SdcaPublic, target.Query);
        Assert.DoesNotContain("PRIVATE", target.Url.AbsoluteUri);
        Assert.DoesNotContain("SUBSYS", target.Url.AbsoluteUri);
        Assert.DoesNotContain("UID_", target.Url.AbsoluteUri);
    }

    [Fact]
    public void GenericBeforeMalformedSpecificCannotManufactureCodecMatch()
    {
        var result = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device(
            [
                Aggregate,
                @"SNDW\CLASS_00&MAN_025D&PART_0711&FAKE_01",
                @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2\PRIVATE"
            ])));

        Assert.Equal(Aggregate, result.Query);
        Assert.False(result.FromCompatibleId);
        Assert.False(result.IsInstallable);
        Assert.False(result.CatalogWasQueried);
    }

    [Fact]
    public void LotsOfGenericAndUnrecognizedIdsDoNotHideLateSpecificModel()
    {
        var hardware = Enumerable.Range(0, 500).Select(i =>
            i % 2 == 0 ? Aggregate : $@"ROOT\UNKNOWN_{i}")
            .Append(Acp)
            .ToArray();
        var result = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device(hardware)));

        Assert.Equal(Acp, result.Query);
        Assert.False(result.FromCompatibleId);
        Assert.DoesNotContain("PRIVATE", result.Url.AbsoluteUri);
    }
}
