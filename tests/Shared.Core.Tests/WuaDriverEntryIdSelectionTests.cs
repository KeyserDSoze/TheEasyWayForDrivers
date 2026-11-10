using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaDriverEntryIdSelectionTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(string id, string instance) =>
        new(instance, "Generic Windows device", "Generic vendor",
            "Display", "Windows", "31.0.0.0", null, "oem17.inf",
            true, true, 0, [id], []);

    private static DriverUpdateInfo Offer(IReadOnlyList<string> ids) =>
        new("00000000-0000-4000-8000-000000000117",
            "Windows Update driver", null, "Display", "Windows",
            null, null, false, "Generic vendor", null,
            @"PCI\VEN_9999&DEV_0000", null,
            AdditionalHardwareIds: ids,
            AdditionalHardwareIdsIncomplete: true);

    [Fact]
    public void SmallWuaCollectionKeepsExactOriginalOrderAndNoTruncation()
    {
        var input = Enumerable.Range(1, 1024)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var chosen = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(input, chosen.HardwareIds);
        Assert.Equal(0, chosen.DeferredIds);
        Assert.Equal(WuaDriverEntryLimits.MaximumAdditionalIds,
            chosen.HardwareIds.Count);
        Assert.Empty(WuaDriverEntryIdSelection.Select([]).HardwareIds);
    }

    [Fact]
    public void LateNvidiaKillerAndRealtekIdsAreKeptDespite1024TransportCap()
    {
        var leading = Enumerable.Range(1, 1600)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var ids = leading.Concat([Nvidia, Killer, Realtek]).ToArray();

        var chosen = WuaDriverEntryIdSelection.Select(ids);

        Assert.Equal(1024, chosen.HardwareIds.Count);
        Assert.Equal(579, chosen.DeferredIds);
        Assert.Equal(leading.Take(960), chosen.HardwareIds.Take(960));
        Assert.Contains(Nvidia, chosen.HardwareIds);
        Assert.Contains(Killer, chosen.HardwareIds);
        Assert.Contains(Realtek, chosen.HardwareIds);
        Assert.Equal(chosen.HardwareIds.Count,
            chosen.HardwareIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var update = Offer(chosen.HardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Nvidia, "PRIVATE_GPU"), update));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Killer, "PRIVATE_KILLER"), update));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Realtek, "PRIVATE_REALTEK"), update));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_9999&DEV_0700", "PRIVATE_OTHER"), update));
    }

    [Fact]
    public void SevenVendorGroupsAreAllRepresentedWhenRawCollectionIsHuge()
    {
        var uninteresting = Enumerable.Range(1, 1500)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var majors = new[]
        {
            Nvidia,
            Killer,
            Realtek,
            @"PCI\VEN_1002&DEV_164E",
            @"PCI\VEN_17CB&DEV_1103",
            @"PCI\VEN_14E4&DEV_43A0",
            @"PCI\VEN_14C3&DEV_0616"
        };
        var chosen = WuaDriverEntryIdSelection.Select(
            uninteresting.Concat(majors).ToArray());

        Assert.Equal(1024, chosen.HardwareIds.Count);
        Assert.All(majors, id => Assert.Contains(id, chosen.HardwareIds));
        Assert.All(chosen.HardwareIds.Take(960),
            id => Assert.StartsWith(@"PCI\VEN_9999", id));
    }

    [Fact]
    public void RoundRobinPreventsFiftyNvidiaEntriesFromHidingLastRealtek()
    {
        var leading = Enumerable.Range(1, 1400)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}");
        var nvidiaFlood = Enumerable.Range(0, 250)
            .Select(i => $@"PCI\VEN_10DE&DEV_{i:X4}");
        var ids = leading.Concat(nvidiaFlood)
            .Append(Killer).Append(Realtek).ToArray();
        var chosen = WuaDriverEntryIdSelection.Select(ids);

        Assert.Equal(1024, chosen.HardwareIds.Count);
        Assert.Contains(Killer, chosen.HardwareIds);
        Assert.Contains(Realtek, chosen.HardwareIds);
        Assert.Contains(@"PCI\VEN_10DE&DEV_0000", chosen.HardwareIds);
        Assert.Equal(628, chosen.DeferredIds);
    }

    [Fact]
    public void Exactly2560EntriesRemainsBoundedAndMoreFailsClosed()
    {
        var ids = Enumerable.Range(1, WuaDriverEntryLimits.MaximumEntriesToInspect)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var chosen = WuaDriverEntryIdSelection.Select(ids);
        Assert.Equal(1024, chosen.HardwareIds.Count);
        Assert.Equal(ids.Length - 1024, chosen.DeferredIds);
        Assert.Throws<InvalidDataException>(() =>
            WuaDriverEntryIdSelection.Select(
                ids.Append(Nvidia).ToArray()));
    }

    [Fact]
    public void UnrecognizedVenPrefixesDoNotDisplaceGenuineMajorVendorIds()
    {
        var leading = Enumerable.Range(1, 1440)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}");
        var fakeNvidia = @"PCI\VEN_10DE0&DEV_28A0";
        var fakeRealtek = @"PCI\VEN_10EC0&DEV_9999";
        var ids = leading.Concat([fakeNvidia, fakeRealtek, Nvidia,
            Realtek, Killer]).ToArray();

        var chosen = WuaDriverEntryIdSelection.Select(ids);

        Assert.Contains(Nvidia, chosen.HardwareIds);
        Assert.Contains(Realtek, chosen.HardwareIds);
        Assert.Contains(Killer, chosen.HardwareIds);
        Assert.DoesNotContain(fakeNvidia, chosen.HardwareIds);
        Assert.DoesNotContain(fakeRealtek, chosen.HardwareIds);
    }

    [Fact]
    public void LateDchAcpiAndModernAudioIdsAreKeptAndExactlyCorrelated()
    {
        var generic = Enumerable.Range(1, 1500)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}");
        // Names, manufacturer and update title are intentionally generic:
        // each association must come solely from exact validated WUA IDs.
        var modern = new[]
        {
            @"ACPI\NVDA0820",
            @"SWC\VEN_10DE&DEV_28A0",
            @"INTELAUDIO\FUNC_01&VEN_8086&DEV_280B",
            @"SWC\VEN_8086&DEV_1234",
            @"HDAUDIO\FUNC_01&VEN_8086&DEV_280B",
            @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295",
            @"SWC\VEN_10EC&DEV_1234",
            @"USB\VID_05C6&PID_1234"
        };
        var input = generic.Concat(modern).ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.Equal(input.Length - 1024, selected.DeferredIds);
        Assert.Equal(input.Take(960), selected.HardwareIds.Take(960));
        var update = Offer(selected.HardwareIds);
        foreach (var id in modern)
        {
            Assert.Contains(id, selected.HardwareIds);
            Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
                Device(id, "PRIVATE_DEVICE"), update));
        }
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"ACPI\NVDA0821", "PRIVATE_OTHER"), update));
        // Correlating eight device IDs never creates extra WUA update GUIDs.
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([update]));
    }

    [Fact]
    public void SimilarLookingAcpiSwcAndAudioVendorPrefixesGetNoReservedSlots()
    {
        var generic = Enumerable.Range(1, 1500)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}");
        var nearMisses = new[]
        {
            @"ACPI\NVDA0820X",
            @"ACPI\NVDA08G0",
            @"ACPI\NVDA082",
            @"SWC\VEN_10DE0&DEV_28A0",
            @"SWC\VEN_80860&DEV_1234",
            @"SWC\VEN_10EC0&DEV_1234",
            @"INTELAUDIO\FUNC_01&VEN_80860&DEV_280B",
            @"INTELAUDIO\FUNC_01&VEN_10EC0&DEV_0295",
            @"USB\VID_05C60&PID_1234"
        };
        var actual = @"ACPI\NVDA0820";
        var selected = WuaDriverEntryIdSelection.Select(
            generic.Concat(nearMisses).Append(actual).ToArray());

        Assert.Contains(actual, selected.HardwareIds);
        Assert.All(nearMisses, id =>
            Assert.DoesNotContain(id, selected.HardwareIds));
        Assert.Equal(1024, selected.HardwareIds.Count);
    }

    [Fact]
    public void InsufficientVendorIdsFillRestFromOriginalWuaOrder()
    {
        var leading = Enumerable.Range(1, 1080)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var chosen = WuaDriverEntryIdSelection.Select(
            leading.Append(Realtek).ToArray());

        Assert.Equal(1024, chosen.HardwareIds.Count);
        Assert.Contains(Realtek, chosen.HardwareIds);
        Assert.Contains(leading[960], chosen.HardwareIds);
        Assert.DoesNotContain(leading[^1], chosen.HardwareIds);
        Assert.Equal(57, chosen.DeferredIds);
    }
    [Fact]
    public void IncompleteMajorVendorTokensCannotExhaustLateWuaReservedSlots()
    {
        // The old prefix test treated every PCI\\VEN_10DE&FAKE_... as
        // NVIDIA and consumed the entire 64-ID vendor priority budget,
        // so a genuine RTX ID after this flood was silently dropped.
        var leading = Enumerable.Range(1, 1350)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var incomplete = Enumerable.Range(1, 140)
            .Select(i => $@"PCI\VEN_10DE&FAKE_{i:X4}")
            .Concat([
                @"PCI\VEN_168C&DEV_001",
                @"USB\VID_05C6&FAKE_0012",
                @"SWC\VEN_10DE&REV_01",
                @"HDAUDIO\FUNC_01&VEN_10EC&FAKE_0295",
                @"ACPI\NVDA0820&UID_0001"
            ]).ToArray();
        var genuine = new[]
        {
            Nvidia, Killer, Realtek,
            @"PCI\VEN_17CB&DEV_1103",
            @"PCI\VEN_14C3&DEV_0616",
            @"USB\VID_05C6&PID_1234",
            @"SWC\VEN_10DE&DEV_28A0",
            @"ACPI\NVDA0820&REV_01"
        };

        var input = leading.Concat(incomplete).Concat(genuine).ToArray();
        var chosen = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(1024, chosen.HardwareIds.Count);
        Assert.Equal(input.Length - 1024, chosen.DeferredIds);
        Assert.Equal(leading.Take(960), chosen.HardwareIds.Take(960));
        Assert.All(incomplete, id =>
            Assert.DoesNotContain(id, chosen.HardwareIds));
        Assert.All(genuine, id =>
            Assert.Contains(id, chosen.HardwareIds));
        Assert.Equal(chosen.HardwareIds.Count,
            chosen.HardwareIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var diagnosticOnly = Offer(chosen.HardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Nvidia, "PRIVATE_GPU"), diagnosticOnly));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Killer, "PRIVATE_KILLER"), diagnosticOnly));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_10DE&FAKE_0001", "PRIVATE_INVALID"),
            diagnosticOnly));
    }


    [Fact]
    public void RareVendorsAndSpecificHidSurviveAfterSameVendorFlood()
    {
        var common = Enumerable.Range(1, 1540)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var rare = new[]
        {
            @"PCI\VEN_1B4B&DEV_9230",
            @"PCI\VEN_144D&DEV_A808",
            @"USB\VID_046D&PID_C534",
            @"HID\VID_046D&PID_C534&MI_00&COL01",
            @"HDAUDIO\FUNC_01&VEN_1234&DEV_5678",
            @"SWC\VEN_1234&DEV_5678"
        };
        var input = common.Concat(rare).ToArray();
        var chosen = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(1024, chosen.HardwareIds.Count);
        Assert.Equal(input.Length - 1024, chosen.DeferredIds);
        Assert.Equal(common.Take(960), chosen.HardwareIds.Take(960));
        Assert.All(rare, id => Assert.Contains(id, chosen.HardwareIds));
        Assert.Equal(1024, chosen.HardwareIds.Distinct(
            StringComparer.OrdinalIgnoreCase).Count());

        var update = Offer(chosen.HardwareIds);
        foreach (var id in rare)
        {
            Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
                Device(id, "PRIVATE_RARE_DEVICE"), update));
        }
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([update]));
    }

    [Fact]
    public void GenericAndMalformedOtherIdsNeverConsumeProtectedSlots()
    {
        var leading = Enumerable.Range(1, 1410)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var untrusted = Enumerable.Range(1, 120)
            .Select(i => $@"PCI\VEN_1B4B&FAKE_{i:X4}")
            .Concat([
                @"HID\HID_DEVICE_SYSTEM_KEYBOARD",
                @"HID\VID_046D",
                @"HID\VID_046D&PID_C534&FAKE_01",
                @"USB\VID_046D",
                @"USB\VID_046D&PID_C534&FAKE_01",
                @"PCI\VEN_144D",
                @"HDAUDIO\FUNC_01&VEN_1234"
            ]).ToArray();
        var genuine = new[]
        {
            @"PCI\VEN_1B4B&DEV_9230",
            @"USB\VID_046D&PID_C534",
            @"HID\VID_046D&PID_C534",
            @"PCI\VEN_144D&DEV_A808"
        };
        var chosen = WuaDriverEntryIdSelection.Select(
            leading.Concat(untrusted).Concat(genuine).ToArray());

        Assert.All(genuine, id => Assert.Contains(id, chosen.HardwareIds));
        Assert.All(untrusted, id => Assert.DoesNotContain(id, chosen.HardwareIds));
        Assert.Equal(1024, chosen.HardwareIds.Count);
    }

    [Fact]
    public void EightExistingPriorityGroupsAndRareHidPciUsbStayFair()
    {
        var generic = Enumerable.Range(1, 1520)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}");
        var flooded = Enumerable.Range(1521, 180)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}");
        var allGroups = new[]
        {
            Nvidia, Killer, Realtek,
            @"PCI\VEN_1002&DEV_164E",
            @"PCI\VEN_17CB&DEV_1103",
            @"PCI\VEN_14E4&DEV_43A0",
            @"PCI\VEN_14C3&DEV_0616",
            @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711",
            @"PCI\VEN_144D&DEV_A808",
            @"HID\VID_046D&PID_C534",
            @"USB\VID_046D&PID_C534"
        };
        var chosen = WuaDriverEntryIdSelection.Select(
            generic.Concat(flooded).Concat(allGroups).ToArray());

        Assert.Equal(1024, chosen.HardwareIds.Count);
        Assert.All(allGroups, id => Assert.Contains(id, chosen.HardwareIds));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"HID\VID_046D", "PRIVATE_BAD_ID"),
            Offer(chosen.HardwareIds)));
    }

    [Fact]
    public void GroupForRareModelsAlsoWorksAfterConfirmedWuaPassMerge()
    {
        var first = Enumerable.Range(1, 1024)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}");
        var second = Enumerable.Range(1025, 1018)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .Concat([
                @"PCI\VEN_144D&DEV_A808",
                @"HID\VID_046D&PID_C534",
                @"USB\VID_046D&PID_C534"
            ]);
        var result = WuaDriverEntryIdSelection.SelectMerged(
            first.Concat(second).ToArray());

        Assert.Equal(1024, result.HardwareIds.Count);
        Assert.True(result.DeferredIds > 0);
        Assert.Contains(@"PCI\VEN_144D&DEV_A808", result.HardwareIds);
        Assert.Contains(@"HID\VID_046D&PID_C534", result.HardwareIds);
        Assert.Contains(@"USB\VID_046D&PID_C534", result.HardwareIds);
        Assert.Throws<InvalidDataException>(() =>
            WuaDriverEntryIdSelection.SelectMerged(
                Enumerable.Range(1, 5122)
                    .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray()));
    }

}
