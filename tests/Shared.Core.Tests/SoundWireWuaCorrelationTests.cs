using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class SoundWireWuaCorrelationTests
{
    private const string Codec =
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711&TYPE_04&VER_01&ADR_01&LID_00&UID_00&SUBSYS_305610EC";
    private const string CodecModel = @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711";
    private const string Aggregate = @"SOUNDWIRE\AGGREGATOR";
    private const string SpeakerAggregate = @"SOUNDWIRE\AGGREGATEDSPEAKER";

    private static DriverInfo Device(string id, bool compatible = false) =>
        new("PRIVATE_SOUNDWIRE_INSTANCE", "Generic Windows audio",
            "Unknown vendor", "MEDIA", "Windows", "1.0", null,
            "oem123.inf", true, true, 0,
            compatible ? [] : [id], compatible ? [id] : []);

    private static DriverUpdateInfo Wua(string id, int number = 1) =>
        new($"00000000-0000-4000-8000-{number:D12}",
            "Windows Audio Driver", null, "MEDIA", "Windows",
            null, null, false, "Windows", null, id, null,
            SearchSource: "Windows Update online");

    private static string Inf(string id) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\n%Maker%=Models,NTamd64\n" +
        "[Models.NTamd64]\n%Audio%=Install," + id + "\n";

    [Theory]
    [InlineData(Aggregate)]
    [InlineData(SpeakerAggregate)]
    [InlineData(@"SOUNDWIRE\AGGREGATOR&UID_00")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711&UID_04")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711&TYPE_04&VER_01&ADR_01&LID_00&UID_00&SERIAL_1234")]
    public void AggregateAndIncompleteSoundWireCannotCorrelateWuaOrInf(
        string generic)
    {
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(generic));
        var device = Device(generic);
        var offer = Wua(generic);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, offer));
        var matching = DeviceWuaMatchGroups.Create(device, [offer]);
        Assert.Empty(matching.Ordinary);
        Assert.Empty(matching.ReviewOnly);
        Assert.Empty(matching.ModelOnlyReview!);
        Assert.Empty(InfModelEvidence.ReadMatchingIds(Inf(generic), [device]));
        Assert.Equal(0, InfModelEvidence.MatchStrength(
            device, InfModelEvidence.ReadDeclaredIds(Inf(generic))));
    }

    [Theory]
    [InlineData(CodecModel)]
    [InlineData(Codec)]
    public void SpecificSoundWireCodecsRetainExactAndCompatibleEvidence(
        string specific)
    {
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(specific));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(specific), Wua(specific)));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            Device(specific, compatible: true), Wua(specific)));
        Assert.Equal(2, InfModelEvidence.MatchStrength(
            Device(specific),
            InfModelEvidence.ReadDeclaredIds(Inf(specific))));
        Assert.Equal(1, DeviceWuaMatchGroups.Create(
            Device(specific), [Wua(specific)]).TotalCorrelated);
    }

    [Fact]
    public void SoundWirePublicModelDoesNotInventExactSubsysMatch()
    {
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(CodecModel));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(Codec), Wua(CodecModel)));
        Assert.Empty(DeviceWuaMatchGroups.Create(
            Device(Codec), [Wua(CodecModel)]).Ordinary);
        Assert.False(MicrosoftUpdateCatalogSearch.Create(
            Device(Codec))!.IsInstallable);
    }

    [Fact]
    public void LateCodecIdsSurviveWuaCapAlongsideAllSevenMajorVendorGroups()
    {
        var filler = Enumerable.Range(1, 1500)
            .Select(n => $@"PCI\VEN_9999&DEV_{n:X4}");
        var majors = new[]
        {
            @"PCI\VEN_10DE&DEV_28A0",
            @"PCI\VEN_8086&DEV_51F1",
            @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295",
            @"PCI\VEN_1002&DEV_164E",
            @"PCI\VEN_17CB&DEV_1103",
            @"PCI\VEN_14E4&DEV_43A0",
            @"PCI\VEN_14C3&DEV_0616",
            Codec, CodecModel
        };
        var rejected = new[]
        {
            Aggregate, SpeakerAggregate,
            @"SOUNDWIRE\SDCA_06&MAN_025D",
            @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711&UID_04"
        };
        var input = filler.Concat(rejected).Concat(majors).ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);
        Assert.Equal(WuaDriverEntryLimits.MaximumAdditionalIds,
            selected.HardwareIds.Count);
        Assert.Equal(input.Length - selected.HardwareIds.Count,
            selected.DeferredIds);
        Assert.All(majors, id => Assert.Contains(id, selected.HardwareIds));
        Assert.All(rejected, id => Assert.DoesNotContain(id, selected.HardwareIds));
        Assert.Equal(input.Take(960), selected.HardwareIds.Take(960));
        var candidate = Wua(@"PCI\VEN_9999&DEV_0001") with
        {
            AdditionalHardwareIds = selected.HardwareIds,
            AdditionalHardwareIdsIncomplete = selected.DeferredIds > 0
        };
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Codec), candidate));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(Aggregate), candidate));
        Assert.Equal(1, QuickUpdatePolicy.AllOrdinaryIds([candidate]).Count);
    }

    [Fact]
    public void GenericSoundWireWuaGuidRemainsUnattributedInAnonymousAudit()
    {
        var report = new DriverSearchReport([Wua(Aggregate)],
        [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "PRIVATE_PATH")
        ]);
        var audit = WuaPnpAssociationAudit.Create([Device(Aggregate)],
            report, true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(0, audit.CompatibleOnly);
        Assert.Equal(0, audit.GenericModelOnly);
        Assert.Equal(1, audit.Unattributed);
        Assert.False(audit.IsInstallable);
        Assert.DoesNotContain("PRIVATE", audit.Text);
        Assert.DoesNotContain("AGGREGATOR", audit.Text);
        Assert.DoesNotContain("00000000-0000-4000", audit.Text);
    }
}
