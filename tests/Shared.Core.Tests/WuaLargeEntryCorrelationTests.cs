using System.Text.Json;
using System.Text.Json.Nodes;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>Real WUA driver GUIDs may carry hundreds of applicable
/// IWindowsDriverUpdateEntry IDs; the same cap must apply at service, merge,
/// matching, JSON transport and human-facing coverage.</summary>
public sealed class WuaLargeEntryCorrelationTests
{
    private static DriverInfo Device(string id) =>
        new("PRIVATE_DEVICE_INSTANCE_123", "Generic Windows Device",
            "Microsoft", "Display", "Microsoft", "31.0.15.6000",
            null, "oem5.inf", true, true, 0, [id], []);

    private static DriverUpdateInfo Update(
        string id, string primary, IReadOnlyList<string> extra,
        bool advanced = false, bool incomplete = false) =>
        new(id, "NVIDIA - Display", null, "Display", "NVIDIA", null,
            null, false, "NVIDIA", "GeForce RTX", primary, null,
            IsAdvancedCandidate: advanced, SearchSource: "Windows Update online",
            AdditionalHardwareIds: extra,
            AdditionalHardwareIdsIncomplete: incomplete);

    private static IReadOnlyList<string> Ids(int start, int count) =>
        Enumerable.Range(start, count)
            .Select(i => $@"PCI\VEN_8086&DEV_{i:X4}&SUBSYS_00108086")
            .ToArray();

    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Wifi = @"PCI\VEN_8086&DEV_51F1&SUBSYS_11118086";

    [Fact]
    public void ExactNvidiaAndKillerMatchesAfter128AreNowCorrelated()
    {
        var ids = Ids(0, 220).Concat([Gpu, Wifi]).ToArray();
        var offer = Update(Guid.NewGuid().ToString("D"),
            @"PCI\VEN_8086&DEV_0000&SUBSYS_00108086", ids);

        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Gpu), offer));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Wifi), offer));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_10DE0&DEV_28A0&SUBSYS_16A217AA"), offer));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"PCI\VEN_8086&DEV_51F0&SUBSYS_11118086"), offer));
    }

    [Fact]
    public void TwoWuaPassesMerge300EachIntoOneUpdateAndKeepLateModel()
    {
        var guid = Guid.NewGuid().ToString("D");
        var first = Update(guid,
            @"PCI\VEN_8086&DEV_0000&SUBSYS_00108086",
            Ids(1, 250), advanced: true);
        var second = Update(guid,
            @"PCI\VEN_8086&DEV_00FA&SUBSYS_00108086",
            Ids(251, 249).Concat([Gpu, Wifi]).ToArray());

        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(merged, [first]));
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(merged, [second]));
        Assert.Single(merged);
        var offer = merged[guid];
        Assert.False(offer.IsAdvancedCandidate);
        Assert.False(offer.AdditionalHardwareIdsIncomplete);
        Assert.Equal(501, offer.AdditionalHardwareIds?.Count);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(Device(Gpu), offer));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(Device(Wifi), offer));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([offer]));
    }

    [Fact]
    public void OverflowAtMergeSetsIncompleteAndNeverCreatesNewUpdateGuid()
    {
        var guid = Guid.NewGuid().ToString("D");
        var first = Update(guid, @"PCI\VEN_8086&DEV_0000", Ids(1, 350));
        var second = Update(guid, @"PCI\VEN_8086&DEV_015F", Ids(351, 350));
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(merged, [first]));
        Assert.Equal(0, WindowsDriverCandidateMerge.Merge(merged, [second]));

        var offer = Assert.Single(merged.Values);
        Assert.True(offer.AdditionalHardwareIdsIncomplete);
        Assert.Equal(WuaDriverEntryLimits.MaximumAdditionalIds,
            offer.AdditionalHardwareIds?.Count);
        Assert.Single(merged);

        var report = new DriverSearchReport([offer],
        [
            new("Windows configurato", "Completed", 1, "OK", 1)
        ]);
        var coverage = ScanCoverageText.Create(report, [], 1, 0, 0,
            [Device(Gpu)], inventoryComplete: true);
        Assert.Contains("associazioni dispositivo incomplete: 1", coverage);
        Assert.DoesNotContain("PRIVATE_DEVICE", coverage);
        Assert.DoesNotContain("SUBSYS", coverage);
        Assert.DoesNotContain(offer.Id, coverage);
    }

    [Fact]
    public void FirstSeenWuaUpdateAlsoNormalizesUntrustedOversizedEntryList()
    {
        var original = Update(Guid.NewGuid().ToString("D"),
            @"PCI\VEN_8086&DEV_0000", Ids(1, 600));
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WindowsDriverCandidateMerge.Merge(merged, [original]));
        var result = Assert.Single(merged.Values);
        Assert.True(result.AdditionalHardwareIdsIncomplete);
        Assert.Equal(512, result.AdditionalHardwareIds?.Count);
    }

    [Fact]
    public void OldJsonWithoutAssociationMetadataRemainsCompatible()
    {
        var original = Update(Guid.NewGuid().ToString("D"),
            @"PCI\VEN_10DE&DEV_28A0", [Gpu], incomplete: true);
        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<DriverUpdateInfo>(json);
        Assert.NotNull(restored);
        Assert.True(restored.AdditionalHardwareIdsIncomplete);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(Device(Gpu), restored));

        var legacy = JsonNode.Parse(json)!.AsObject();
        Assert.True(legacy.Remove(nameof(DriverUpdateInfo.AdditionalHardwareIdsIncomplete)));
        var old = JsonSerializer.Deserialize<DriverUpdateInfo>(legacy.ToJsonString());
        Assert.NotNull(old);
        Assert.False(old.AdditionalHardwareIdsIncomplete);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(Device(Gpu), old));
    }

    [Fact]
    public void MergeKeepsPriorIncompleteEvidenceWhenRecommendedWins()
    {
        var id = Guid.NewGuid().ToString("D");
        var old = Update(id, @"PCI\VEN_8086&DEV_0000",
            [Wifi], advanced: true, incomplete: true);
        var recommended = Update(id, Gpu, [Wifi]);
        var merged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(merged, [old]);
        WindowsDriverCandidateMerge.Merge(merged, [recommended]);
        var result = Assert.Single(merged.Values);

        Assert.True(result.AdditionalHardwareIdsIncomplete);
        Assert.False(result.IsAdvancedCandidate);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(Device(Gpu), result));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(Device(Wifi), result));
    }
}
