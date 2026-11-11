using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// WUA's 64 reserved transport positions must not be monopolized by
/// distinct uncommon PCI manufacturer families ahead of actual SNDW/ACP
/// codec models. Nothing here manufactures a Windows Update offer.
/// </summary>
public sealed class WuaModernAudioRareVendorFloodTests
{
    private const string Sndw =
        @"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&SUBSYS_09831028";
    private const string Acp =
        @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2";
    private const string Sdca =
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711";
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";

    private static string[] Common(int count) =>
        Enumerable.Range(1, count)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .ToArray();

    private static string[] RareVendors(int count) =>
        Enumerable.Range(0, count)
            .Select(i => $@"PCI\VEN_{(0x2000 + i):X4}&DEV_0001")
            .ToArray();

    private static DriverInfo Device(string id) =>
        new("PRIVATE_INSTANCE_SERIAL", "Generic codec", "Microsoft",
            "MEDIA", "Microsoft", null, null, null,
            false, true, 0, [id], []);

    private static DriverUpdateInfo Offer(IReadOnlyList<string> ids) =>
        new("00000000-0000-4000-8000-000000000362",
            "Generic Windows driver", null, "MEDIA", "Microsoft",
            null, null, false, "Microsoft", null,
            @"PCI\VEN_9999&DEV_0001", null,
            SearchSource: "Windows Update online",
            AdditionalHardwareIds: ids,
            AdditionalHardwareIdsIncomplete: true);

    private static string Inf(string id) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\n%Vendor%=Models,NTamd64\n" +
        "[Models.NTamd64]\n%Device%=Install," + id + "\n";

    [Fact]
    public void Over64RareManufacturersCannotBuryLateSndwAcpOrSdca()
    {
        // The first 960 stay stable, followed by 110 DIFFERENT PCI
        // manufacturer families. Previously those 110 filled all 64
        // slots in the shared "other" bucket, losing late audio IDs.
        var leading = Common(1250);
        var rare = RareVendors(110);
        var input = leading.Concat(rare)
            .Concat([Sndw, Acp, Sdca, Nvidia]).ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.Equal(input.Length - 1024, selected.DeferredIds);
        Assert.Equal(leading.Take(960), selected.HardwareIds.Take(960));
        Assert.Contains(Sndw, selected.HardwareIds);
        Assert.Contains(Acp, selected.HardwareIds);
        Assert.Contains(Sdca, selected.HardwareIds);
        Assert.Contains(Nvidia, selected.HardwareIds);
        Assert.Contains(rare[0], selected.HardwareIds);
        Assert.Equal(1024, selected.HardwareIds.Distinct(
            StringComparer.OrdinalIgnoreCase).Count());

        var offer = Offer(selected.HardwareIds);
        foreach (var exact in new[] { Sndw, Acp, Sdca, Nvidia })
        {
            Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
                Device(exact), offer));
            Assert.Equal(2, InfModelEvidence.MatchStrength(
                Device(exact), InfModelEvidence.ReadDeclaredIds(Inf(exact))));
        }
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([offer]));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"SNDW\CLASS_00"), offer));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"ACP\DEVTYPE_0005"), offer));
    }

    [Fact]
    public void IncompleteOrPrivateAudioIdsDoNotConsumeDedicatedSlots()
    {
        var invalid = new[]
        {
            @"SNDW\CLASS_00",
            @"SNDW\CLASS_00&MAN_025D",
            @"SNDW\CLASS_00&MAN_025D&PART_071",
            @"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02",
            @"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&SUBSYS_0983102",
            @"SNDW\CLASS_00&MAN_025D&PART_0711\PRIVATE_SERIAL",
            @"ACP\DEVTYPE_0005",
            @"ACP\DEVTYPE_0005&DEVREV_0000",
            @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022",
            @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2&FAKE_01",
            @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2\PRIVATE_INSTANCE"
        };
        var input = Common(1250).Concat(RareVendors(110))
            .Concat(invalid).Concat([Sndw, Acp]).ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.All(invalid, id =>
            Assert.DoesNotContain(id, selected.HardwareIds));
        Assert.Contains(Sndw, selected.HardwareIds);
        Assert.Contains(Acp, selected.HardwareIds);
        Assert.True(selected.DeferredIds > 0);
        Assert.DoesNotContain(selected.HardwareIds,
            id => id.Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Fact]
    public void FirstSndwMakerFloodCannotDisplaceAnotherSpecificAudioFamily()
    {
        var sameFamily = Enumerable.Range(0, 180)
            .Select(i => $@"SNDW\CLASS_00&MAN_025D&PART_{i:X4}");
        const string secondFamily = @"SNDW\CLASS_01&MAN_0242&PART_0811";
        var input = Common(1170).Concat(sameFamily)
            .Concat([secondFamily, Sndw, Acp]).ToArray();

        var selected = WuaDriverEntryIdSelection.Select(input);
        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.Contains(secondFamily, selected.HardwareIds);
        Assert.Contains(Acp, selected.HardwareIds);
        Assert.Contains(@"SNDW\CLASS_00&MAN_025D&PART_0000",
            selected.HardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(secondFamily), Offer(selected.HardwareIds)));
    }

    [Fact]
    public void OriginalOrderAndBoundedInputRemainUnchanged()
    {
        var input = Common(1024);
        var unchanged = WuaDriverEntryIdSelection.Select(input);
        Assert.Equal(input, unchanged.HardwareIds);
        Assert.Equal(0, unchanged.DeferredIds);

        var huge = Common(WuaDriverEntryLimits.MaximumEntriesToInspect + 1);
        Assert.Throws<InvalidDataException>(() =>
            WuaDriverEntryIdSelection.Select(huge));
    }
}
