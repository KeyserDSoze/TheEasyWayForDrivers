using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #436: a large WUA entry collection must distinguish two validated HID
/// products under the same VID, even if one product has many revisions.
/// Product-aware selection is diagnostic transport, never install approval.
/// </summary>
public sealed class WuaHidProductFairnessTests
{
    private const string LogitechFirst =
        @"HID\VID_046D&PID_C534&REV_0100";
    private const string LogitechSecond =
        @"HID\VID_046D&PID_C52B&MI_00&COL01";
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Guid = "00000000-0000-4000-8000-000000000436";

    private static string[] Common(int count) =>
        Enumerable.Range(1, count)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .ToArray();

    private static string[] Revisions(int count) =>
        Enumerable.Range(0, count)
            .Select(i => $@"HID\VID_046D&PID_C534&REV_{(0x100 + i):X4}")
            .ToArray();

    private static string[] RarePci(int count) =>
        Enumerable.Range(0, count)
            .Select(i => $@"PCI\VEN_{(0x3000 + i):X4}&DEV_0001")
            .ToArray();

    private static DriverInfo Device(string id) =>
        new("PRIVATE_HID_INSTANCE", "Windows HID Device", "Microsoft",
            "HIDClass", "Microsoft", "1", null, "oem1.inf", true, true,
            0, [id], []);

    private static DriverUpdateInfo Wua(IReadOnlyList<string> ids,
        string source = "Windows Update online") =>
        new(Guid, "Windows device driver", null, "HIDClass", "Microsoft",
            null, null, false, "Microsoft", null,
            @"PCI\VEN_9999&DEV_0000", null,
            SearchSource: source, AdditionalHardwareIds: ids);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentLateHidPidSurvivesFirstProductRevisionFlood(
        bool firstProductAlreadyInBaseline)
    {
        var prefix = firstProductAlreadyInBaseline
            ? new[] { LogitechFirst }.Concat(Common(959))
            : Common(960).AsEnumerable();
        var input = prefix.Concat(Revisions(160))
            .Concat([LogitechSecond])
            .Concat(RarePci(120))
            .Concat([Nvidia])
            .ToArray();

        var result = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(1024, result.HardwareIds.Count);
        Assert.Equal(input.Length - 1024, result.DeferredIds);
        Assert.Equal(input.Take(960), result.HardwareIds.Take(960));
        Assert.Contains(LogitechSecond, result.HardwareIds);
        Assert.Contains(Nvidia, result.HardwareIds);
        Assert.Contains(LogitechFirst, result.HardwareIds);
        Assert.Equal(1024, result.HardwareIds.Distinct(
            StringComparer.OrdinalIgnoreCase).Count());

        var offer = Wua(result.HardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(LogitechSecond), offer));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"HID\VID_046D&PID_C52C"), offer));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"HID\VID_046D"), offer));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([offer]));
    }

    [Fact]
    public void SameGuidFromTwoWuaSourcesPreservesSecondProductAsAnExactId()
    {
        // A late second product belongs to a different successfully queried
        // source but the SAME update GUID, not a new Windows Update offer.
        var first = Wua(Common(960).Concat(Revisions(160)).ToArray(),
            "Windows configurato");
        var second = Wua(new[] { LogitechSecond }
            .Concat(RarePci(120)).ToArray(),
            "Microsoft Update online");

        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(
            merged, [first, second]));
        var offer = Assert.Single(merged.Values);
        Assert.Equal(Guid, offer.Id);
        Assert.Equal(1024, offer.AdditionalHardwareIds?.Count);
        Assert.Contains(LogitechSecond, offer.AdditionalHardwareIds!);
        Assert.True(offer.AdditionalHardwareIdsIncomplete);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(LogitechSecond), offer));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"HID\VID_046D&PID_C52C"), offer));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([offer]));
    }

    [Theory]
    [InlineData(@"HID\VID_046D")]
    [InlineData(@"HID\HID_DEVICE")]
    [InlineData(@"HID\VID_046D&UP:0001_U:0002")]
    [InlineData(@"HID\VID_046D&PID_C52B&FAKE_01")]
    [InlineData(@"HID\VID_046D&PID_C52B\PRIVATE_INSTANCE")]
    [InlineData(@"HID\VID_046D&PID_C52B ")]
    public void GenericMalformedOrPrivateHidCannotConsumeProductSlot(
        string invalid)
    {
        Assert.False(HidPnpMatchPolicy.IsSpecificHardwareId(invalid));
        var input = Common(960).Concat([invalid, LogitechSecond])
            .Concat(RarePci(120)).ToArray();

        var result = WuaDriverEntryIdSelection.Select(input);
        Assert.DoesNotContain(invalid, result.HardwareIds);
        Assert.Contains(LogitechSecond, result.HardwareIds);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(LogitechSecond), Wua(result.HardwareIds)));
    }

    [Fact]
    public void SmallerEntryCollectionsKeepOriginalIdsAndOrder()
    {
        string[] ids =
        [
            LogitechFirst, LogitechSecond,
            @"HID\VID_1532&PID_0067&REV_0001",
            @"HID\VID_046D",
            Nvidia
        ];

        var result = WuaDriverEntryIdSelection.Select(ids);
        Assert.Equal(ids, result.HardwareIds);
        Assert.Equal(0, result.DeferredIds);
        Assert.False(HidPnpMatchPolicy.IsSpecificHardwareId(ids[3]));
        Assert.True(HidPnpMatchPolicy.IsSpecificHardwareId(ids[2]));
        Assert.Throws<InvalidDataException>(() =>
            WuaDriverEntryIdSelection.Select(
                Common(WuaDriverEntryLimits.MaximumEntriesToInspect + 1)));
    }
}
