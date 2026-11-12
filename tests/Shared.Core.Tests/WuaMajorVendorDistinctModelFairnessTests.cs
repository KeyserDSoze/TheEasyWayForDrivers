using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Preserves distinct, already-declared WUA PnP models even when hundreds
/// of the same vendor/model's SUBSYS variants precede them in a bounded
/// 1024-ID transport preview. Never creates or installs a driver update.
/// </summary>
public sealed class WuaMajorVendorDistinctModelFairnessTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string OldGpu = @"PCI\VEN_10DE&DEV_1F15";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Intel = @"PCI\VEN_8086&DEV_1234";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Audio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string Swc = @"SWC\VEN_10DE&DEV_28A0";
    private const string Acpi = @"ACPI\NVDA0820";

    private static string[] Fill(int count, int offset = 1) =>
        Enumerable.Range(offset, count)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();

    private static string[] Subsystems(string model, int count) =>
        Enumerable.Range(1, count)
            .Select(i => model + $"&SUBSYS_{i:X8}").ToArray();

    private static DriverInfo Device(string hardwareId) =>
        new("PRIVATE_DEVICE_INSTANCE", "Windows generic device",
            "Microsoft", "System", "Microsoft", "1.0", null,
            "PRIVATE_oem42.inf", true, true, 0, [hardwareId], []);

    private static DriverUpdateInfo Offer(IReadOnlyList<string> additional) =>
        new("00000000-0000-4000-8000-000000000391",
            "WUA driver", null, "System", "Microsoft",
            null, null, false, "Microsoft", null,
            @"PCI\VEN_9999&DEV_0000", null,
            SearchSource: "Windows Update online",
            AdditionalHardwareIds: additional,
            AdditionalHardwareIdsIncomplete: true);

    [Fact]
    public void NvidiaAndKillerAfterFourHundredModelVariantsAreRetained()
    {
        var baseline = Fill(960);
        var nvidiaVariants = Subsystems(OldGpu, 240);
        var intelVariants = Subsystems(Intel, 220);
        var input = baseline.Concat(nvidiaVariants).Concat(intelVariants)
            .Concat([Nvidia, Killer, Realtek, Audio, Swc, Acpi]).ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.Equal(input.Length - 1024, selected.DeferredIds);
        Assert.Equal(baseline, selected.HardwareIds.Take(960));
        Assert.All(new[] { Nvidia, Killer, Realtek, Audio, Swc, Acpi,
            nvidiaVariants[0], intelVariants[0] },
            id => Assert.Contains(id, selected.HardwareIds));
        var offer = Offer(selected.HardwareIds);
        foreach (var id in new[] { Nvidia, Killer, Realtek, Audio, Swc, Acpi })
            Assert.Equal(2, DriverUpdateMatcher.MatchStrength(Device(id), offer));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_10DE&DEV_28A1"), offer));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([offer]));
        Assert.DoesNotContain("PRIVATE", string.Join(" ", selected.HardwareIds));
    }

    [Fact]
    public void First960AlreadyContainNvidiaBusAndOldModel()
    {
        var baseline = Fill(959).Append(OldGpu).ToArray();
        var input = baseline.Concat(Subsystems(OldGpu, 380))
            .Concat([Nvidia, Killer]).ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);
        Assert.Equal(baseline, selected.HardwareIds.Take(960));
        Assert.Contains(Nvidia, selected.HardwareIds);
        Assert.Contains(Killer, selected.HardwareIds);
        Assert.True(selected.DeferredIds > 0);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Nvidia), Offer(selected.HardwareIds)));
    }

    [Fact]
    public void CrossSourceMergedWuaPassAlsoPreservesLateSpecificModels()
    {
        var input = Fill(2048).Concat(Subsystems(OldGpu, 400))
            .Concat(Subsystems(Intel, 380))
            .Concat([Nvidia, Killer, Realtek]).ToArray();
        var selected = WuaDriverEntryIdSelection.SelectMerged(input);
        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.Equal(input.Length - 1024, selected.DeferredIds);
        Assert.All(new[] { Nvidia, Killer, Realtek },
            id => Assert.Contains(id, selected.HardwareIds));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(
            [Offer(selected.HardwareIds)]));
    }

    [Fact]
    public void RealtekQualcommBroadcomAndMediaTekKeepVendorFairness()
    {
        var input = Fill(960)
            .Concat(Subsystems(OldGpu, 200))
            .Concat(Subsystems(Intel, 200))
            .Concat(Subsystems(@"PCI\VEN_10EC&DEV_8168", 200))
            .Concat([Nvidia, Killer,
                @"PCI\VEN_10EC&DEV_8125",
                @"PCI\VEN_17CB&DEV_1103",
                @"PCI\VEN_14E4&DEV_43A0",
                @"PCI\VEN_14C3&DEV_0616"])
            .ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);
        Assert.All(new[] { Nvidia, Killer,
            @"PCI\VEN_10EC&DEV_8125",
            @"PCI\VEN_17CB&DEV_1103",
            @"PCI\VEN_14E4&DEV_43A0",
            @"PCI\VEN_14C3&DEV_0616" },
            id => Assert.Contains(id, selected.HardwareIds));
        Assert.Equal(1024, selected.HardwareIds.Count);
    }

    [Fact]
    public void InvalidVendorSuffixAndPrivateInstanceNeverTakePriority()
    {
        var invalid = Enumerable.Range(1, 200)
            .Select(i => $@"PCI\VEN_10DE&DEV_1F15&FAKE_{i:X4}")
            .Concat([
                @"PCI\VEN_10DE&DEV_28A0\PRIVATE_ORDER",
                @"PCI\VEN_10DE&DEV_28A0&SUBSYS_123",
                @"PCI\VEN_8086&DEV_51F1&SUBSYS_NOTHEX",
                @"SWC\VEN_10DE&REV_01"
            ]).ToArray();
        var result = WuaDriverEntryIdSelection.Select(
            Fill(960).Concat(invalid).Concat([Nvidia, Killer]).ToArray());
        Assert.Contains(Nvidia, result.HardwareIds);
        Assert.Contains(Killer, result.HardwareIds);
        // Unsupported original WUA strings may still fill unused IPC
        // slots in the historical fallback. They must NEVER consume
        // priority slots or become a correlatable exact PnP identity.
        Assert.Equal(Nvidia, result.HardwareIds[960]);
        Assert.Equal(Killer, result.HardwareIds[961]);
        Assert.All(invalid, id =>
            Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(id)));
    }

    [Fact]
    public void SmallInputKeepsOriginalOrderAndAllHardLimits()
    {
        var input = Fill(1019).Concat([OldGpu, Nvidia, Killer, Realtek])
            .ToArray();
        var chosen = WuaDriverEntryIdSelection.Select(input);
        Assert.Equal(input, chosen.HardwareIds);
        Assert.Equal(0, chosen.DeferredIds);
        Assert.Throws<InvalidDataException>(() =>
            WuaDriverEntryIdSelection.Select(
                Fill(WuaDriverEntryLimits.MaximumEntriesToInspect + 1)));
    }
}
