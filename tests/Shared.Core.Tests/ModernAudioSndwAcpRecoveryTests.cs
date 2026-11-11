using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// SNDW and ACP class/type-only identifiers must never impersonate a codec.
/// The only recovery is an exact-device SetupAPI/SignedDriver full model,
/// preserving every family, revision and manufacturer hint supplied by WMI.
/// </summary>
public sealed class ModernAudioSndwAcpRecoveryTests
{
    private const string Sndw = @"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00";
    private const string SndwModel = @"SNDW\CLASS_00&MAN_025D&PART_0711";
    private const string Acp = @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2";
    private const string GuidOne = "00000000-0000-4000-8000-000000000354";

    private static DriverInfo Device(PnpSetupApiIdentity.Enrichment ids) =>
        new("PRIVATE_PNP_INSTANCE_SERIAL", "Generic Windows audio device",
            "Microsoft", "MEDIA", "Microsoft", "1.0", null,
            "PRIVATE_DRIVER.inf", true, true, 0,
            ids.HardwareIds, ids.CompatibleIds,
            PnpIdsRecoveredFromSignedDriver: ids.UsedSignedDriver,
            PnpIdsRecoveredFromSetupApi: ids.UsedSetupApi);

    private static DriverUpdateInfo Wua(string id) =>
        new(GuidOne, "Windows audio driver", null, "MEDIA", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    private static string Inf(string model) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\n%Vendor%=Models,NTamd64\n" +
        "[Models.NTamd64]\n%Codec%=Install," + model + "\n";

    [Theory]
    [InlineData(Sndw)]
    [InlineData(SndwModel)]
    [InlineData(Acp)]
    [InlineData(@"sndw\class_00&man_025d&part_0711&ver_02&uid_00")]
    [InlineData(@"acp\devtype_0005&devrev_0000&ven_1022&dev_15e2")]
    public void CompleteModernAudioIdRemainsExactWuaAndInfEvidence(string model)
    {
        Assert.True(ModernAudioPnpMatchPolicy.IsSpecificHardwareId(model));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(model));
        Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery([model]));

        var enriched = PnpSetupApiIdentity.Merge([model], [],
            [@"PCI\VEN_10DE&DEV_28A0"], [], Sndw, null);
        var device = Device(enriched);
        Assert.Equal([model], enriched.HardwareIds);
        Assert.False(enriched.UsedSetupApi);
        Assert.False(enriched.UsedSignedDriver);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(device, Wua(model)));
        Assert.Equal(2, InfModelEvidence.MatchStrength(device,
            InfModelEvidence.ReadDeclaredIds(Inf(model))));
        Assert.False(QuickUpdatePolicy.IsEligible(
            Wua(model) with { IsAdvancedCandidate = true }));
    }

    [Theory]
    [InlineData(@"SNDW\CLASS_00", Sndw)]
    [InlineData(@"SNDW\CLASS_00&MAN_025D", Sndw)]
    [InlineData(@"sndw\class_00&man_025d", Sndw)]
    [InlineData(@"ACP\DEVTYPE_0005", Acp)]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000", Acp)]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022", Acp)]
    [InlineData(@"acp\devtype_0005&devrev_0000&ven_1022", Acp)]
    public void IncompleteWmiCanRecoverSpecificModelOnlyFromSameInstance(
        string incomplete, string full)
    {
        Assert.True(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [incomplete]));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(incomplete));
        Assert.False(ModernAudioPnpMatchPolicy.IsSpecificHardwareId(incomplete));

        var enrich = PnpSetupApiIdentity.Merge([incomplete], [],
            [full], [], null, null);
        Assert.True(enrich.UsedSetupApi);
        Assert.False(enrich.UsedSignedDriver);
        Assert.Equal(full, enrich.HardwareIds[0]);
        Assert.Contains(incomplete, enrich.HardwareIds);
        var device = Device(enrich);

        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(device, Wua(full)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, Wua(incomplete)));
        Assert.Equal(2, InfModelEvidence.MatchStrength(device,
            InfModelEvidence.ReadDeclaredIds(Inf(full))));
        Assert.Equal(0, InfModelEvidence.MatchStrength(device,
            InfModelEvidence.ReadDeclaredIds(Inf(incomplete))));
        Assert.Single(DeviceWuaMatchGroups.Create(device, [Wua(full)]).Ordinary);

        var report = new DriverSearchReport([Wua(full)],
        [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "PRIVATE_PROXY")
        ]);
        var audit = WuaPnpAssociationAudit.Create([device], report, true);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.False(audit.IsInstallable);
        var copy = ScanCoverageText.Create(
            report, [], 1, 0, 0, [device], true);
        Assert.DoesNotContain("PRIVATE", copy);
        Assert.DoesNotContain(full, copy);
        Assert.DoesNotContain(GuidOne, copy);
    }

    [Theory]
    [InlineData(@"SNDW\CLASS_00", Sndw)]
    [InlineData(@"SNDW\CLASS_00&MAN_025D", Sndw)]
    [InlineData(@"ACP\DEVTYPE_0005", Acp)]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022", Acp)]
    public void SignedDriverOnlyRecoversWhenSameDeviceNativeIsUnavailable(
        string incomplete, string full)
    {
        var enrich = PnpSetupApiIdentity.Merge([incomplete], [],
            [], [], full, null);
        Assert.Equal([full, incomplete], enrich.HardwareIds);
        Assert.True(enrich.UsedSignedDriver);
        Assert.False(enrich.UsedSetupApi);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(enrich), Wua(full)));

        var native = PnpSetupApiIdentity.Merge([incomplete], [],
            [full], [], full.Replace("&PART_0711", "&PART_0001"), null);
        Assert.True(native.UsedSetupApi);
        Assert.False(native.UsedSignedDriver);
        Assert.Equal(full, native.HardwareIds[0]);
    }

    [Theory]
    [InlineData(@"SNDW\CLASS_00&MAN_025D",
        @"SNDW\CLASS_00&MAN_0242&PART_0711")]
    [InlineData(@"SNDW\CLASS_00",
        @"SNDW\CLASS_01&MAN_025D&PART_0711")]
    [InlineData(@"SNDW\CLASS_00", Acp)]
    [InlineData(@"SNDW\CLASS_00&MAN_025D",
        @"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D",
        @"SNDW\CLASS_00&MAN_025D&PART_0711&FAKE_00")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D",
        @"SNDW\CLASS_00&MAN_025D&PART_0711\PRIVATE_SERIAL")]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022",
        @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1023&DEV_15E2")]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000",
        @"ACP\DEVTYPE_0005&DEVREV_0001&VEN_1022&DEV_15E2")]
    [InlineData(@"ACP\DEVTYPE_0005",
        @"ACP\DEVTYPE_0006&DEVREV_0000&VEN_1022&DEV_15E2")]
    [InlineData(@"ACP\DEVTYPE_0005", Sndw)]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022",
        @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2&FAKE_01")]
    public void ForeignBusVendorFamilyRevisionOrFakeSuffixCannotRecover(
        string incomplete, string native)
    {
        Assert.True(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [incomplete]));
        var result = PnpSetupApiIdentity.Merge(
            [incomplete], [], [native], [], null, null);
        Assert.Equal([incomplete], result.HardwareIds);
        Assert.False(result.UsedSetupApi);
        Assert.False(result.UsedSignedDriver);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(result), Wua(native)));

        var signed = PnpSetupApiIdentity.Merge(
            [incomplete], [], [], [], native, null);
        Assert.Equal([incomplete], signed.HardwareIds);
        Assert.False(signed.UsedSignedDriver);
    }

    [Theory]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&FAKE_00")]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2")]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2&REV_01")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D" + "\n")]
    [InlineData(@"ACP\DEVTYPE_0005" + "\u202e")]
    [InlineData(@"SNDW\CLASS_00\PRIVATE_INSTANCE")]
    [InlineData(@"ACP\DEVTYPE_0005\PRIVATE_INSTANCE")]
    public void CompleteOrMalformedWmiNeverTriggersIncompleteModelRecovery(
        string wmi)
    {
        Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [wmi]));
    }

    [Theory]
    [InlineData(@"SNDW\CLASS_00")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D")]
    [InlineData(@"ACP\DEVTYPE_0005")]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000")]
    public void GenericSharedClassIsNeverAnExactWuaOrInfMatch(string generic)
    {
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(generic));
        var device = Device(PnpSetupApiIdentity.Merge([generic], [],
            [], [], null, null));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            device, Wua(generic)));
        Assert.Equal(0, InfModelEvidence.MatchStrength(device,
            InfModelEvidence.ReadDeclaredIds(Inf(generic))));
        Assert.Empty(DeviceWuaMatchGroups.Create(
            device, [Wua(generic)]).Ordinary);

        var report = new DriverSearchReport([Wua(generic)],
        [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "PRIVATE_SOURCE")
        ]);
        var audit = WuaPnpAssociationAudit.Create([device], report, true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(1, audit.Unattributed);
        Assert.False(audit.IsInstallable);
    }

    [Fact]
    public void ConflictingClassOrMakerWmiFailsClosedAndNeverBorrowsModel()
    {
        IReadOnlyList<string>[] incompatible =
        [
            [@"SNDW\CLASS_00", @"SNDW\CLASS_01"],
            [@"SNDW\CLASS_00&MAN_025D", @"SNDW\CLASS_00&MAN_0242"],
            [@"ACP\DEVTYPE_0005", @"ACP\DEVTYPE_0006"],
            [@"ACP\DEVTYPE_0005&DEVREV_0000",
                @"ACP\DEVTYPE_0005&DEVREV_0001"],
            [@"SNDW\CLASS_00", @"ACP\DEVTYPE_0005"],
            [@"SNDW\CLASS_00", @"HDAUDIO\FUNC_01"],
            [@"ACP\DEVTYPE_0005", @"PCI\VEN_10DE&DEV_28A0"]
        ];
        foreach (var ids in incompatible)
        {
            Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
                ids));
            var merge = PnpSetupApiIdentity.Merge(
                ids, [], [Sndw, Acp], [], Sndw, null);
            Assert.Equal(ids, merge.HardwareIds);
            Assert.False(merge.UsedSetupApi);
            Assert.False(merge.UsedSignedDriver);
        }
    }

    [Fact]
    public void CompleteWmiOutranksNativeAndSignedModelOfOtherCodec()
    {
        var merge = PnpSetupApiIdentity.Merge(
            [Sndw], [], [SndwModel], [], Acp, null);
        Assert.Equal([Sndw], merge.HardwareIds);
        Assert.False(merge.UsedSetupApi);
        Assert.False(merge.UsedSignedDriver);
    }

    [Fact]
    public void ManyIrrelevantNativeHardwareIdsCannotHideLaterMatchingCodec()
    {
        var filler = Enumerable.Range(1, 300)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .ToArray();
        var input = filler.Concat([Sndw]).ToArray();
        var merge = PnpSetupApiIdentity.Merge(
            [@"SNDW\CLASS_00&MAN_025D"], [], input, [], null, null);
        Assert.Equal(Sndw, merge.HardwareIds[0]);
        Assert.True(merge.UsedSetupApi);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(merge), Wua(Sndw)));
    }
}
