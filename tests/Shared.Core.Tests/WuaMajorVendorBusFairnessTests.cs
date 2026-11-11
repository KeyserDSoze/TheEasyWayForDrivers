using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Late validated NVIDIA/Intel bus models must survive when hundreds of
/// PCI models from the same recognized manufacturer precede them in one
/// bounded WUA Type=Driver entry list. No driver is manufactured here.
/// </summary>
public sealed class WuaMajorVendorBusFairnessTests
{
    private const string NvidiaGpu = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaAudio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string NvidiaSwc = @"SWC\VEN_10DE&DEV_28A0";
    private const string NvidiaUsb = @"USB\VID_0955&PID_1234";
    private const string NvidiaAcpi = @"ACPI\NVDA0820";
    private const string IntelUsb = @"USB\VID_8087&PID_0033";
    private const string IntelSwc = @"SWC\VEN_8086&DEV_1234";
    private const string IntelAudio = @"INTELAUDIO\FUNC_01&VEN_8086&DEV_280B";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string GuidId = "00000000-0000-4000-8000-000000000365";

    private static string[] Common(int count) =>
        Enumerable.Range(1, count)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .ToArray();

    private static string[] NvidiaPciFlood(int count) =>
        Enumerable.Range(1, count)
            .Select(i => $@"PCI\VEN_10DE&DEV_{i:X4}")
            .ToArray();

    private static string[] IntelPciFlood(int count) =>
        Enumerable.Range(1, count)
            .Select(i => $@"PCI\VEN_8086&DEV_{(i + 0x1000):X4}")
            .ToArray();

    private static DriverInfo Device(string id) =>
        new("PRIVATE_WINDOWS_DEVICE_INSTANCE", "Microsoft generic device",
            "Microsoft", "System", "Microsoft", "1.0.0.0", null,
            "oem42.inf", true, true, 0, [id], []);

    private static DriverUpdateInfo Offer(IReadOnlyList<string> ids) =>
        new(GuidId, "Windows device driver", null, "System",
            "Microsoft", null, null, false, "Microsoft", null,
            @"PCI\VEN_9999&DEV_0000", null,
            SearchSource: "Windows Update online",
            AdditionalHardwareIds: ids,
            AdditionalHardwareIdsIncomplete: true);

    [Fact]
    public void NvidiaPciFloodCannotBuryAudioSwcUsbOrAcpi()
    {
        var common = Common(1250);
        var input = common.Concat(NvidiaPciFlood(205)).Concat(
            [NvidiaAudio, NvidiaSwc, NvidiaUsb, NvidiaAcpi, NvidiaGpu])
            .ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.Equal(input.Length - 1024, selected.DeferredIds);
        Assert.Equal(common.Take(960), selected.HardwareIds.Take(960));
        Assert.Contains(NvidiaAudio, selected.HardwareIds);
        Assert.Contains(NvidiaSwc, selected.HardwareIds);
        Assert.Contains(NvidiaUsb, selected.HardwareIds);
        Assert.Contains(NvidiaAcpi, selected.HardwareIds);
        // One late PCI identity also survives, not only non-PCI buses.
        Assert.Contains(NvidiaPciFlood(1)[0], selected.HardwareIds);
        Assert.Equal(1024, selected.HardwareIds.Distinct(
            StringComparer.OrdinalIgnoreCase).Count());

        var offer = Offer(selected.HardwareIds);
        foreach (var id in new[]
        {
            NvidiaAudio, NvidiaSwc, NvidiaUsb, NvidiaAcpi
        })
        {
            Assert.Equal(2, DriverUpdateMatcher.MatchStrength(Device(id), offer));
        }
        // Multiple exact device IDs always remain ONE WUA update GUID.
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([offer]));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009C"), offer));
    }

    [Fact]
    public void IntelPciFloodCannotBuryBluetoothDchOrAudio()
    {
        var input = Common(1260).Concat(IntelPciFlood(180))
            .Concat([IntelUsb, IntelSwc, IntelAudio, Killer]).ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.Contains(IntelUsb, selected.HardwareIds);
        Assert.Contains(IntelSwc, selected.HardwareIds);
        Assert.Contains(IntelAudio, selected.HardwareIds);
        Assert.Contains(IntelPciFlood(1)[0], selected.HardwareIds);
        var offer = Offer(selected.HardwareIds);
        foreach (var id in new[] { IntelUsb, IntelSwc, IntelAudio })
            Assert.Equal(2, DriverUpdateMatcher.MatchStrength(Device(id), offer));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([offer]));
    }

    [Fact]
    public void CrossSourcePostMergeFairnessPreservesOtherNvidiaBuses()
    {
        var combined = Common(2048).Concat(NvidiaPciFlood(400))
            .Concat([NvidiaAudio, NvidiaSwc, NvidiaAcpi]).ToArray();
        var selected = WuaDriverEntryIdSelection.SelectMerged(combined);

        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.Equal(combined.Length - 1024, selected.DeferredIds);
        Assert.Contains(NvidiaAudio, selected.HardwareIds);
        Assert.Contains(NvidiaSwc, selected.HardwareIds);
        Assert.Contains(NvidiaAcpi, selected.HardwareIds);
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(
            [Offer(selected.HardwareIds)]));
    }

    [Fact]
    public void GenericMalformedOrPrivateLateIdsNeverGetBusSlots()
    {
        var invalid = new[]
        {
            @"PCI\VEN_10DE",
            @"PCI\VEN_10DE&FAKE_28A0",
            @"SWC\VEN_10DE",
            @"SWC\VEN_10DE&DEV_28A0&FAKE_00",
            @"HDAUDIO\FUNC_01&VEN_10DE",
            @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B&FAKE_00",
            @"USB\VID_0955",
            @"USB\VID_0955&PID_1234\PRIVATE_INSTANCE",
            @"ACPI\NVDA0820&UID_0001"
        };
        var input = Common(1250).Concat(NvidiaPciFlood(150))
            .Concat(invalid).Concat([NvidiaAudio, NvidiaAcpi]).ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.All(invalid, id => Assert.DoesNotContain(id, selected.HardwareIds));
        Assert.Contains(NvidiaAudio, selected.HardwareIds);
        Assert.Contains(NvidiaAcpi, selected.HardwareIds);
        Assert.DoesNotContain(selected.HardwareIds,
            id => id.Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Fact]
    public void SmallInputOrderAndLimitsRemainUntouched()
    {
        var ids = Common(1000).Concat([NvidiaAudio, NvidiaSwc]).ToArray();
        var result = WuaDriverEntryIdSelection.Select(ids);
        Assert.Equal(ids, result.HardwareIds);
        Assert.Equal(0, result.DeferredIds);
        Assert.Throws<InvalidDataException>(() =>
            WuaDriverEntryIdSelection.Select(Common(
                WuaDriverEntryLimits.MaximumEntriesToInspect + 1)));
    }
}
