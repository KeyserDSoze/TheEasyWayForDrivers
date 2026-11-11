using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A WUA entry can name many SoundWire SDCA codecs for one SDCA+MAN
/// family; a different late maker still needs one of the 64 transport
/// slots. This tests transport fairness, not driver availability.
/// </summary>
public sealed class WuaSoundWireSdcaFamilyFairnessTests
{
    private const string OtherMaker =
        @"SOUNDWIRE\SDCA_07&MAN_0242&FUNC_0811";
    private const string RealtekModel =
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711";
    private const string RealtekFull =
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711&TYPE_04&VER_01&ADR_01&LID_00&UID_00&SUBSYS_305610EC";
    private const string Sndw =
        @"SNDW\CLASS_00&MAN_025D&PART_0711";
    private const string Acp =
        @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2";
    private const string GuidId = "00000000-0000-4000-8000-000000000366";

    private static string[] Filler(int count) =>
        Enumerable.Range(1, count)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();

    private static string[] SameMaker(int count) =>
        Enumerable.Range(0, count)
            .Select(i => $@"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_{i:X4}")
            .ToArray();

    private static DriverInfo Device(string id) =>
        new("PRIVATE_CODEC_INSTANCE", "Generic Windows audio",
            "Microsoft", "MEDIA", "Microsoft", "1.0", null,
            "oem42.inf", true, true, 0, [id], []);

    private static DriverUpdateInfo Offer(
        IReadOnlyList<string> ids, string source = "Windows Update online") =>
        new(GuidId, "Windows Audio Driver", null, "MEDIA",
            "Microsoft", null, null, false, "Microsoft", null,
            @"PCI\VEN_9999&DEV_0000", null,
            SearchSource: source, AdditionalHardwareIds: ids);

    [Fact]
    public void MoreThanOneHundredFuncVariantsCannotHideAnotherSdcaMaker()
    {
        var input = Filler(1250).Concat(SameMaker(140))
            .Concat([OtherMaker, RealtekFull, Sndw, Acp])
            .ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.Equal(input.Length - 1024, selected.DeferredIds);
        Assert.Equal(input.Take(960), selected.HardwareIds.Take(960));
        Assert.Contains(OtherMaker, selected.HardwareIds);
        Assert.Contains(SameMaker(1)[0], selected.HardwareIds);
        Assert.Contains(Sndw, selected.HardwareIds);
        Assert.Contains(Acp, selected.HardwareIds);
        Assert.Equal(1024, selected.HardwareIds.Distinct(
            StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(selected.HardwareIds.Count(id => id.StartsWith(
            @"SOUNDWIRE\SDCA_06&MAN_025D",
            StringComparison.OrdinalIgnoreCase)) < 64);

        var offer = Offer(selected.HardwareIds) with
        {
            AdditionalHardwareIdsIncomplete = true
        };
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(OtherMaker), offer));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"SOUNDWIRE\SDCA_07&MAN_0242&FUNC_0812"), offer));
        Assert.Single(DeviceWuaMatchGroups.Create(Device(OtherMaker),
            [offer]).Ordinary);
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([offer]));
    }

    [Fact]
    public void InvalidSdcaOrGenericAggregatorDoesNotOccupyFairSlots()
    {
        var malformed = new[]
        {
            @"SOUNDWIRE\AGGREGATOR",
            @"SOUNDWIRE\AGGREGATEDSPEAKER",
            @"SOUNDWIRE\SDCA_07&MAN_0242",
            @"SOUNDWIRE\SDCA_07&MAN_0242&FUNC_0811&UID_00",
            @"SOUNDWIRE\SDCA_07&MAN_0242&FUNC_0811\PRIVATE",
            @"SOUNDWIRE\SDCA_07&MAN_0242&FUNC_0811&TYPE_04&VER_01&ADR_01&LID_00&UID_00&FAKE_01"
        };
        var input = Filler(1200).Concat(SameMaker(150))
            .Concat(malformed).Concat([OtherMaker]).ToArray();
        var result = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(1024, result.HardwareIds.Count);
        Assert.Contains(OtherMaker, result.HardwareIds);
        Assert.All(malformed,
            id => Assert.DoesNotContain(id, result.HardwareIds));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(
            malformed[0]));
        Assert.DoesNotContain(result.HardwareIds,
            id => id.Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Fact]
    public void SameGuidAcrossWuaSourcesRetainsDifferentSdcaFamily()
    {
        var first = Offer(Filler(1024), "Windows configurato");
        var second = Offer(
            SameMaker(140).Concat([OtherMaker, Sndw, Acp]).ToArray(),
            "Microsoft Update online");
        var byGuid = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(byGuid, [first]));
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(byGuid, [second]));

        var update = Assert.Single(byGuid.Values);
        Assert.Equal(GuidId, update.Id);
        Assert.Equal(1024, update.AdditionalHardwareIds!.Count);
        Assert.True(update.AdditionalHardwareIdsIncomplete);
        Assert.Contains(OtherMaker, update.AdditionalHardwareIds);
        Assert.Contains(Sndw, update.AdditionalHardwareIds);
        Assert.Contains(Acp, update.AdditionalHardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(OtherMaker), update));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(byGuid.Values));

        var report = new DriverSearchReport([update],
        [
            new DriverSearchPassStatus("Windows configurato",
                "Completed", 1, "PRIVATE_SOURCE_A"),
            new DriverSearchPassStatus("Microsoft Update online",
                "Completed", 1, "PRIVATE_SOURCE_B")
        ]);
        var audit = WuaPnpAssociationAudit.Create(
            [Device(OtherMaker)], report, true);
        Assert.True(audit.SearchVerified);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(1, audit.ExactHardware);
        Assert.False(audit.IsInstallable);
        Assert.DoesNotContain("PRIVATE", audit.Text);

        var copied = ScanCoverageText.Create(report, [], 1, 0, 0,
            [Device(OtherMaker)], true);
        Assert.Contains("Windows Update: 1 candidati distinti", copied);
        Assert.DoesNotContain("PRIVATE", copied);
        Assert.DoesNotContain(GuidId, copied);
        Assert.DoesNotContain(OtherMaker, copied);
    }

    [Fact]
    public void ShortListsPreserveOrderAndLongInputsKeepTheirBudget()
    {
        var ids = new[]
        {
            @"SOUNDWIRE\AGGREGATOR", RealtekModel, OtherMaker, RealtekFull
        };
        var shortResult = WuaDriverEntryIdSelection.Select(ids);
        Assert.Equal(ids, shortResult.HardwareIds);
        Assert.Equal(0, shortResult.DeferredIds);

        Assert.Throws<InvalidDataException>(() =>
            WuaDriverEntryIdSelection.Select(
                Filler(WuaDriverEntryLimits.MaximumEntriesToInspect + 1)));
    }
}
