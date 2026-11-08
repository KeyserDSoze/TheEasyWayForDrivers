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
    public void Exactly2048EntriesRemainsBoundedAndMoreFailsClosed()
    {
        var ids = Enumerable.Range(1, 2048)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var chosen = WuaDriverEntryIdSelection.Select(ids);
        Assert.Equal(1024, chosen.HardwareIds.Count);
        Assert.Equal(1024, chosen.DeferredIds);
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
}
