using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaInstalledEvidenceIndexTests
{
    private static DriverInfo Device(
        string[] hardware, string[]? compatible = null) =>
        new(@"PCI\VEN_10DE&DEV_28A0\DEVICE_1",
            "Microsoft Basic Display Adapter",
            "Microsoft", "Display", "Microsoft", "32.0.16.1",
            null, null, true, true, 0, hardware, compatible ?? []);

    private static WuaDiagnosticAssociation Update(
        string primary, params string[] additional) =>
        new(Guid.NewGuid().ToString("D"), primary, additional);

    [Fact]
    public void RepeatedDeclaredIdsAndHardwareAliasesCountOneGuid()
    {
        const string gpu = @"PCI\VEN_10DE&DEV_28A0";
        var updates = new[] {
            Update(gpu, gpu.ToLowerInvariant(), gpu, gpu),
            Update(@"PCI\VEN_8086&DEV_51F1")
        };
        var prepared = WuaInstalledEvidenceIndex.Create(updates);
        Assert.True(prepared.UsesIndex);
        Assert.Equal(1, prepared.CountForDevice(
            Device([gpu, gpu], [gpu])));
    }

    [Fact]
    public void CompatibleIdsAreExactAndForeignVendorCannotBeCorrelated()
    {
        const string gpu = @"PCI\VEN_10DE&DEV_28A0";
        var updates = new[] {
            Update(gpu),
            Update(@"PCI\VEN_10DE&DEV_28A1"),
            Update(@"PCI\VEN_8086&DEV_51F1"),
            Update(@"PCI\VEN_10DE")
        };
        var prepared = WuaInstalledEvidenceIndex.Create(updates);
        Assert.Equal(1, prepared.CountForDevice(
            Device([@"ROOT\DISPLAY"], [gpu])));
        Assert.Equal(0, prepared.CountForDevice(
            Device([@"PCI\VEN_10EC&DEV_8125"])));
        Assert.Equal(0, prepared.CountForDevice(
            Device([@"ROOT\DISPLAY"])));
    }

    [Fact]
    public void OverBudgetIndexFallsBackWithoutDiscardingAnyExactMatch()
    {
        const string gpu = @"PCI\VEN_10DE&DEV_28A0";
        var updates = new[] {
            Update(gpu),
            Update(@"PCI\VEN_8086&DEV_51F1", gpu),
            Update(@"PCI\VEN_10DE&DEV_28A1")
        };
        var device = Device([gpu]);
        var indexed = WuaInstalledEvidenceIndex.Create(updates);
        var fallback = WuaInstalledEvidenceIndex.Create(
            updates, maxIndexedAssociations: 1);
        Assert.True(indexed.UsesIndex);
        Assert.False(fallback.UsesIndex);
        Assert.Equal(2, indexed.CountForDevice(device));
        Assert.Equal(indexed.CountForDevice(device),
            fallback.CountForDevice(device));
    }

    [Fact]
    public void UnknownOrMalformedIdsDoNotCreateWeakModelMatches()
    {
        var updates = new[] {
            Update(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_11111111"),
            Update(@"PCI\VEN_10DE&DEV_28A0&FAKE_1"),
            Update(@"PCI\VEN_10DE&DEV_28A0"),
        };
        var prepared = WuaInstalledEvidenceIndex.Create(updates);
        Assert.Equal(1, prepared.CountForDevice(
            Device([@"PCI\VEN_10DE&DEV_28A0"])));
        Assert.Equal(0, prepared.CountForDevice(
            Device([@"PCI\VEN_10DE&DEV_28A0&SUBSYS_22222222"])));
    }

    [Fact]
    public void DuplicateSourceGuidUsesMergedIdSetAndIsNotCountedTwice()
    {
        var guid = Guid.NewGuid().ToString("D");
        var first = new DriverUpdateInfo(
            guid, "NVIDIA Driver", null, "Display", "NVIDIA",
            "32.0.16.1", null, false, "NVIDIA", null,
            @"PCI\VEN_10DE&DEV_28A0", null,
            SearchSource: "Windows Update online");
        var second = first with {
            HardwareId = @"PCI\VEN_8086&DEV_51F1",
            SearchSource = "Microsoft Update online"
        };
        var merged = WuaDiagnosticAssociations.Create([first, second]);
        var prepared = WuaInstalledEvidenceIndex.Create(merged);
        Assert.Single(merged);
        Assert.Equal(1, prepared.CountForDevice(
            Device([@"PCI\VEN_10DE&DEV_28A0"])));
        Assert.Equal(1, prepared.CountForDevice(
            Device([@"PCI\VEN_8086&DEV_51F1"])));
    }

    [Fact]
    public void IndexedResultsAgreeWithAuthoritativeMatcherForAllDevices()
    {
        var ids = new[] {
            @"PCI\VEN_10DE&DEV_28A0",
            @"PCI\VEN_8086&DEV_51F1",
            @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B",
            @"USB\VID_8087&PID_0033",
            @"PCI\VEN_10EC&DEV_8125"
        };
        var wua = ids.Select(id => Update(id)).ToArray();
        var prepared = WuaInstalledEvidenceIndex.Create(wua);
        foreach (var id in ids)
        {
            var device = Device([id]);
            var exhaustive = wua.Count(x => x.Matches(device));
            Assert.Equal(exhaustive, prepared.CountForDevice(device));
        }
    }
}
