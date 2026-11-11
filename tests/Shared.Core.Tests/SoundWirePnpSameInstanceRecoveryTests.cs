using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A partial WMI SoundWire identity can be enriched ONLY from the exact
/// PnP instance's SetupAPI/SignedDriver metadata, and never by name,
/// another bus, incompatible SDCA family or foreign manufacturer.
/// </summary>
public sealed class SoundWirePnpSameInstanceRecoveryTests
{
    private const string Codec =
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711&TYPE_04&VER_01&ADR_01&LID_00&UID_00&SUBSYS_305610EC";
    private const string Model =
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711";
    private const string OtherMaker =
        @"SOUNDWIRE\SDCA_06&MAN_0242&FUNC_0711";
    private const string OtherSdca =
        @"SOUNDWIRE\SDCA_07&MAN_025D&FUNC_0711";
    private const string Gpu =
        @"PCI\VEN_10DE&DEV_28A0";
    private const string Audio =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string UpdateGuid =
        "00000000-0000-4000-8000-000000000353";

    private static DriverInfo Device(
        PnpSetupApiIdentity.Enrichment ids) =>
        new(@"SOUNDWIRE\PRIVATE_INSTANCE_SERIAL",
            "Generic Windows audio device", "Microsoft", "MEDIA",
            "Microsoft", "1.0", null, "PRIVATE_OEM_DRIVER.inf",
            true, true, 0, ids.HardwareIds, ids.CompatibleIds,
            PnpIdsRecoveredFromSignedDriver: ids.UsedSignedDriver,
            PnpIdsRecoveredFromSetupApi: ids.UsedSetupApi);

    private static DriverUpdateInfo Wua(string id) =>
        new(UpdateGuid, "Windows audio driver", null, "MEDIA",
            "Microsoft", null, null, false, "Microsoft",
            null, id, null, SearchSource: "Windows Update online");

    private static string Inf(string id) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\n%Vendor%=Models,NTamd64\n" +
        "[Models.NTamd64]\n%Codec%=Install," + id + "\n";

    [Theory]
    [InlineData(@"SOUNDWIRE\AGGREGATOR")]
    [InlineData(@"SOUNDWIRE\AGGREGATEDSPEAKER")]
    [InlineData(@"SOUNDWIRE\SDCA_06")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D")]
    [InlineData(@"soundwire\sdca_06&man_025d")]
    public void SameInstanceNativeCodecRestoresExactSoundWireModelFromGenericWmi(
        string generic)
    {
        Assert.True(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [generic]));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(generic));

        var merged = PnpSetupApiIdentity.Merge(
            [generic], [], [Codec], [], null, null);
        var driver = Device(merged);

        Assert.True(merged.UsedSetupApi);
        Assert.False(merged.UsedSignedDriver);
        Assert.Equal(Codec, merged.HardwareIds[0]);
        Assert.Contains(generic, merged.HardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(driver, Wua(Codec)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(driver, Wua(generic)));
        Assert.True(driver.PnpIdsRecoveredFromSetupApi);
        Assert.False(driver.PnpIdsRecoveredFromSignedDriver);

        var infMatches = InfModelEvidence.ReadMatchingIds(Inf(Codec), [driver]);
        Assert.Equal(2, InfModelEvidence.MatchStrength(driver, infMatches));
        Assert.Single(DeviceWuaMatchGroups.Create(
            driver, [Wua(Codec)]).Ordinary);

        var report = new DriverSearchReport([Wua(Codec)],
        [
            new DriverSearchPassStatus(
                "Windows Update online", "Completed", 1,
                "PRIVATE_PROXY_AND_INSTANCE")
        ]);
        var audit = WuaPnpAssociationAudit.Create([driver], report, true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(0, audit.Unattributed);
        Assert.False(audit.IsInstallable);

        var copy = ScanCoverageText.Create(
            report, [], 1, 0, 0, [driver], inventoryComplete: true);
        Assert.DoesNotContain("PRIVATE", copy);
        Assert.DoesNotContain(Codec, copy);
        Assert.DoesNotContain(UpdateGuid, copy);
        Assert.Contains("Windows Update: 1 candidati distinti", copy);
    }

    [Theory]
    [InlineData(@"SOUNDWIRE\AGGREGATOR")]
    [InlineData(@"SOUNDWIRE\SDCA_06")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D")]
    public void ExactSameInstanceSignedDriverCanRecoverWhenNativeIsUnavailable(
        string generic)
    {
        var merged = PnpSetupApiIdentity.Merge(
            [generic], [], [], [], Codec, null);
        Assert.Equal([Codec, generic], merged.HardwareIds);
        Assert.False(merged.UsedSetupApi);
        Assert.True(merged.UsedSignedDriver);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(merged), Wua(Codec)));
    }

    [Fact]
    public void ValidNativeModelAlwaysOutranksDifferentSignedDriverModel()
    {
        var merged = PnpSetupApiIdentity.Merge(
            [@"SOUNDWIRE\SDCA_06&MAN_025D"], [],
            [Codec], [], OtherMaker, null);
        Assert.Equal([Codec, @"SOUNDWIRE\SDCA_06&MAN_025D"],
            merged.HardwareIds);
        Assert.True(merged.UsedSetupApi);
        Assert.False(merged.UsedSignedDriver);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(merged), Wua(OtherMaker)));
    }

    [Theory]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D",
        OtherMaker)]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D",
        OtherSdca)]
    [InlineData(@"SOUNDWIRE\SDCA_06", OtherSdca)]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D", Gpu)]
    [InlineData(@"SOUNDWIRE\AGGREGATOR", Audio)]
    [InlineData(@"SOUNDWIRE\AGGREGATEDSPEAKER",
        @"SOUNDWIRE\AGGREGATOR")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D",
        @"SOUNDWIRE\SDCA_06&MAN_025D")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D",
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711&FAKE_01")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D",
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711\PRIVATE_SERIAL")]
    public void ForeignBusMakerSdcaOrMalformedNativeCannotSupplyCodec(
        string wmi, string invalidNative)
    {
        Assert.True(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [wmi]));
        var merged = PnpSetupApiIdentity.Merge(
            [wmi], [], [invalidNative], [], null, null);

        Assert.Equal([wmi], merged.HardwareIds);
        Assert.False(merged.UsedSetupApi);
        Assert.False(merged.UsedSignedDriver);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(merged), Wua(Codec)));
    }

    [Theory]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D",
        OtherMaker)]
    [InlineData(@"SOUNDWIRE\SDCA_06", OtherSdca)]
    [InlineData(@"SOUNDWIRE\AGGREGATOR", Gpu)]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D",
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711&SERIAL_0001")]
    public void ForeignSignedDriverIdentityAlsoFailsClosed(
        string generic, string invalidSigned)
    {
        var merged = PnpSetupApiIdentity.Merge(
            [generic], [], [], [], invalidSigned, null);

        Assert.Equal([generic], merged.HardwareIds);
        Assert.False(merged.UsedSignedDriver);
        Assert.False(merged.UsedSetupApi);
    }

    [Theory]
    [InlineData(@"SOUNDWIRE\AGGREGATOR&UID_00")]
    [InlineData(@"SOUNDWIRE\AGGREGATEDSPEAKER&FAKE_00")]
    [InlineData(@"SOUNDWIRE\SDCA_0")]
    [InlineData(@"SOUNDWIRE\SDCA_000")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D&EXTRA_00")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711&UID_00")]
    public void CompleteMalformedSoundWireWmiNeverTriggersNativeOverride(
        string wmi)
    {
        Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [wmi]));
        var merged = PnpSetupApiIdentity.Merge(
            [wmi], [], [Codec], [], Codec, null);
        Assert.False(merged.UsedSetupApi);
        Assert.False(merged.UsedSignedDriver);
        Assert.DoesNotContain(Codec, merged.HardwareIds);
    }

    [Theory]
    [InlineData(@"SOUNDWIRE\AGGREGATOR\PRIVATE_SERIAL")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D" + "\n")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D" + "\u202e")]
    public void MalformedWmiIsDiscardedBeforeExistingSameInstanceFallback(
        string malformed)
    {
        // Existing PnP inventory policy: invalid WMI data is equivalent
        // to no usable WMI HardwareID. The exact-instance SetupAPI lookup
        // may still supply its separately validated SDCA model; it does
        // not validate or promote the malformed WMI token.
        Assert.Empty(PnpSetupApiIdentity.SanitizeWmiIds([malformed]));
        Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [malformed]));

        var merged = PnpSetupApiIdentity.Merge(
            [malformed], [], [Codec], [], Codec, null);
        Assert.Equal([Codec], merged.HardwareIds);
        Assert.True(merged.UsedSetupApi);
        Assert.False(merged.UsedSignedDriver);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(merged), Wua(Codec)));
        Assert.DoesNotContain(malformed, merged.HardwareIds);
    }

    [Fact]
    public void ConflictingAndMixedPartialSoundWireIdsCannotBorrowNativeCodec()
    {
        IReadOnlyList<string>[] ambiguous =
        [
            [@"SOUNDWIRE\SDCA_06&MAN_025D",
             @"SOUNDWIRE\SDCA_06&MAN_0242"],
            [@"SOUNDWIRE\SDCA_06",
             @"SOUNDWIRE\SDCA_07"],
            [@"SOUNDWIRE\AGGREGATOR", @"HDAUDIO\FUNC_01"],
            [@"SOUNDWIRE\AGGREGATOR", Gpu]
        ];

        foreach (var ids in ambiguous)
        {
            Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
                ids));
            var merged = PnpSetupApiIdentity.Merge(
                ids, [], [Codec], [], Codec, null);
            Assert.Equal(ids, merged.HardwareIds);
            Assert.False(merged.UsedSetupApi);
            Assert.False(merged.UsedSignedDriver);
        }
    }

    [Fact]
    public void GenericOnlySoundWireIsNeverAConfirmedWuaCodecMatch()
    {
        var noNative = PnpSetupApiIdentity.Merge(
            [@"SOUNDWIRE\AGGREGATOR"], [], [], [], null, null);
        var device = Device(noNative);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            device, Wua(@"SOUNDWIRE\AGGREGATOR")));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            device, Wua(Codec)));
        Assert.Empty(DeviceWuaMatchGroups.Create(
            device, [Wua(Codec)]).Ordinary);

        var audit = WuaPnpAssociationAudit.Create(
            [device],
            new DriverSearchReport([Wua(Codec)],
            [
                new DriverSearchPassStatus(
                    "Windows Update online", "Completed", 1, "OK")
            ]), true);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(1, audit.Unattributed);
        Assert.False(audit.IsInstallable);
    }
}
