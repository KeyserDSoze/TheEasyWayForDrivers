using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A completed WUA COM pass can contain thousands of entry Hardware IDs.
/// Preserve specific SNDW and ACP audio models beyond the 1024-ID IPC
/// transport cap, but NEVER reserve positions for class/type-only tokens.
/// </summary>
public sealed class WuaModernAudioFairTransportTests
{
    private const string Sndw =
        @"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00";
    private const string SndwOtherFamily =
        @"SNDW\CLASS_01&MAN_0242&PART_0811";
    private const string Acp =
        @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2";
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string GuidOne = "00000000-0000-4000-8000-000000000356";

    private static DriverInfo Device(string id, string instance) =>
        new(instance, "Generic Windows audio", "Microsoft", "MEDIA",
            "Microsoft", "1.0", null, "PRIVATE_OEM.inf",
            true, true, 0, [id], []);

    private static DriverUpdateInfo Offer(
        IReadOnlyList<string> ids, string source = "Windows Update online") =>
        new(GuidOne, "Windows Audio Driver", null, "MEDIA",
            "Microsoft", null, null, false, "Microsoft", null,
            @"PCI\VEN_9999&DEV_0000", null,
            SearchSource: source,
            AdditionalHardwareIds: ids,
            AdditionalHardwareIdsIncomplete: true);

    private static string[] Flood(int count, int first = 1) =>
        Enumerable.Range(first, count)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .ToArray();

    [Fact]
    public void TwoModernAudioBusesSurviveLateBeyond1024WithoutExtraWuaGuids()
    {
        var legacy = Flood(1740);
        var ids = legacy.Concat([Sndw, Acp, Nvidia]).ToArray();
        var selected = WuaDriverEntryIdSelection.Select(ids);

        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.Equal(ids.Length - 1024, selected.DeferredIds);
        Assert.Equal(legacy.Take(960), selected.HardwareIds.Take(960));
        Assert.Contains(Sndw, selected.HardwareIds);
        Assert.Contains(Acp, selected.HardwareIds);
        Assert.Contains(Nvidia, selected.HardwareIds);
        Assert.Equal(1024, selected.HardwareIds.Distinct(
            StringComparer.OrdinalIgnoreCase).Count());

        var offer = Offer(selected.HardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Sndw, "PRIVATE_SOUNDWIRE_SERIAL"), offer));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Acp, "PRIVATE_AMD_SERIAL"), offer));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"SNDW\CLASS_00", "PRIVATE_GENERIC"), offer));

        var grouped = DeviceWuaMatchGroups.Prepare([offer]);
        Assert.Equal(1, grouped.UniqueGuidCount);
        Assert.Single(grouped.ForDevice(
            Device(Sndw, "PRIVATE_SOUNDWIRE_SERIAL")).Ordinary);
        Assert.Single(grouped.ForDevice(
            Device(Acp, "PRIVATE_AMD_SERIAL")).Ordinary);
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([offer]));
    }

    [Fact]
    public void AnotherSndwFamilyAndAcpStayFairAfterSameFamilyFlood()
    {
        var leading = Flood(1230);
        var sameFamily = Enumerable.Range(0, 205)
            .Select(i => $@"SNDW\CLASS_00&MAN_025D&PART_{i:X4}");
        var input = leading.Concat(sameFamily).Concat(
            [SndwOtherFamily, Acp, Sndw]).ToArray();

        var selected = WuaDriverEntryIdSelection.Select(input);
        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.Equal(input.Length - 1024, selected.DeferredIds);
        Assert.Contains(@"SNDW\CLASS_00&MAN_025D&PART_0000",
            selected.HardwareIds);
        Assert.Contains(SndwOtherFamily, selected.HardwareIds);
        Assert.Contains(Acp, selected.HardwareIds);

        // SNDW's first family is represented and its 200+ repeated
        // parts cannot hide a different maker's or ACP vendor's model.
        Assert.True(selected.HardwareIds.Count(x =>
            x.StartsWith(@"SNDW\CLASS_00&MAN_025D",
                StringComparison.OrdinalIgnoreCase)) < 65);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(SndwOtherFamily, "PRIVATE_OTHER_CODEC"),
            Offer(selected.HardwareIds)));
    }

    [Fact]
    public void MalformedAndGenericSndwAcpCannotConsumeProtectedSlots()
    {
        var malformed = new[]
        {
            @"SNDW\CLASS_00",
            @"SNDW\CLASS_00&MAN_025D",
            @"SNDW\CLASS_00&MAN_025D&PART_071",
            @"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02",
            @"SNDW\CLASS_00&MAN_025D&PART_0711&FAKE_00",
            @"SNDW\CLASS_00&MAN_025D&PART_0711\PRIVATE_SERIAL",
            @"ACP\DEVTYPE_0005",
            @"ACP\DEVTYPE_0005&DEVREV_0000",
            @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022",
            @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E",
            @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2&FAKE_01",
            @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2\PRIVATE",
            @"C:\PRIVATE_ORDER\driver.inf"
        };
        var input = Flood(1550).Concat(malformed)
            .Concat([Sndw, Acp, Nvidia]).ToArray();

        var selected = WuaDriverEntryIdSelection.Select(input);
        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.All(malformed,
            id => Assert.DoesNotContain(id, selected.HardwareIds));
        Assert.Contains(Sndw, selected.HardwareIds);
        Assert.Contains(Acp, selected.HardwareIds);
        Assert.Contains(Nvidia, selected.HardwareIds);
        Assert.True(selected.DeferredIds > 0);
        Assert.DoesNotContain(selected.HardwareIds,
            id => id.Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Fact]
    public void DistinctWuaPassesMergeSameGuidAndKeepLateModernAudioIds()
    {
        // Each confirmed source is independently within IPC's 1024-ID
        // limit. Late SNDW/ACP from the second completed source must
        // survive the FINAL same-GUID merge, not become "missing drivers".
        var passA = Offer(Flood(1024),
            "Windows configurato");
        var passB = Offer(Flood(1022, 1025).Concat(
            [Sndw, Acp]).ToArray(), "Microsoft Update online");
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(merged, [passA]));
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(merged, [passB]));

        var result = Assert.Single(merged.Values);
        Assert.Equal(GuidOne, result.Id);
        Assert.Equal(1024, result.AdditionalHardwareIds!.Count);
        Assert.Contains(Sndw, result.AdditionalHardwareIds);
        Assert.Contains(Acp, result.AdditionalHardwareIds);
        Assert.True(result.AdditionalHardwareIdsIncomplete);
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([result]));

        var devices = new[]
        {
            Device(Sndw, "PRIVATE_CODEC"),
            Device(Acp, "PRIVATE_AMD_AUDIO")
        };
        var search = new DriverSearchReport([result],
        [
            new DriverSearchPassStatus("Windows configurato",
                "Completed", 1, "PRIVATE_SOURCE_A"),
            new DriverSearchPassStatus("Microsoft Update online",
                "Completed", 1, "PRIVATE_SOURCE_B")
        ]);
        var audit = WuaPnpAssociationAudit.Create(devices, search, true);
        Assert.True(audit.SearchVerified);
        Assert.Equal(1, audit.ConfirmedGuids);
        // Audit measures matched GUIDs, not the number of devices
        // matched by that single WUA driver package.
        Assert.Equal(1, audit.ExactHardware);
        Assert.All(devices, device =>
            Assert.Single(DeviceWuaMatchGroups.Create(
                device, [result]).Ordinary));
        Assert.Equal(1, audit.IncompleteWuaIds);
        Assert.False(audit.IsInstallable);

        var copy = ScanCoverageText.Create(search, [], 2, 0, 0,
            devices, true);
        Assert.Contains("Windows Update: 1 candidati distinti", copy);
        Assert.DoesNotContain("PRIVATE", copy);
        Assert.DoesNotContain(GuidOne, copy);
        Assert.DoesNotContain(Sndw, copy);
        Assert.DoesNotContain(Acp, copy);
    }

    [Fact]
    public void MaximumInputBudgetAndOriginalSmallOrderAreUnchanged()
    {
        var shortInput = new[] { @"SNDW\CLASS_00", Acp, Sndw };
        Assert.Equal(shortInput,
            WuaDriverEntryIdSelection.Select(shortInput).HardwareIds);
        Assert.Equal(0,
            WuaDriverEntryIdSelection.Select(shortInput).DeferredIds);

        var max = Flood(WuaDriverEntryLimits.MaximumEntriesToInspect - 2)
            .Concat([Sndw, Acp]).ToArray();
        var selected = WuaDriverEntryIdSelection.Select(max);
        Assert.Equal(WuaDriverEntryLimits.MaximumAdditionalIds,
            selected.HardwareIds.Count);
        Assert.Contains(Sndw, selected.HardwareIds);
        Assert.Contains(Acp, selected.HardwareIds);
        Assert.Throws<InvalidDataException>(() =>
            WuaDriverEntryIdSelection.Select(max.Append(Nvidia).ToArray()));
    }
}
