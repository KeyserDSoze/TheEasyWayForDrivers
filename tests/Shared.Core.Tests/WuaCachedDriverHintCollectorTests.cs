using System.Text.Json;
using System.Text.Json.Nodes;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaCachedDriverHintCollectorTests
{
    private static string Id(int index) =>
        $"00000000-0000-4000-8000-{index:D12}";

    private static WuaCachedDriverHint Hint(int index) =>
        WuaCachedDriverHintCollector.TryCreate(Id(index),
            $"NVIDIA - Display - 32.0.16.{1000 + index}")!;

    [Fact]
    public void LocalCacheTitleIsNeverConsideredCurrentDriverOfferOrInstallable()
    {
        var hint = Hint(1);
        Assert.False(hint.IsInstallable);
        Assert.False(hint.IsVerifiedAvailable);
        Assert.Contains("storico", hint.Detail);
        Assert.Contains("NON verificate", hint.Detail);
        Assert.Contains("Aggiorna tutto", hint.Detail);

        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows configurato", "Completed", 0, "")
        ], CachedDriverHints: [hint], CachedDriverSources: [
            new DriverSearchPassStatus(
                "Cache Windows locale · non installati", "Completed", 1, ""),
            new DriverSearchPassStatus(
                "Cache Windows locale · già installati", "Completed", 0, "")
        ]);
        Assert.Empty(report.Updates);
        Assert.Single(report.CachedDriverReviewItems);
        Assert.False(report.IsPartial);
        Assert.False(report.AllFailed);
        Assert.False(report.CachedDriverSearchFailed);
    }

    [Fact]
    public void DeduplicatesCacheGuidsAndExcludesConfirmedOnlineDriverIds()
    {
        var result = new Dictionary<string, WuaCachedDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        var candidates = new[]
        {
            Hint(1), Hint(1) with { UpdateId = Id(1).ToUpperInvariant() },
            Hint(2), Hint(3)
        };
        var added = WuaCachedDriverHintCollector.Merge(
            result, candidates, [Id(2)], out var omitted);
        Assert.Equal(2, added);
        Assert.Equal(0, omitted);
        Assert.Equal(2, result.Count);
        Assert.False(result.ContainsKey(Id(2)));
    }

    [Fact]
    public void MoreThanThirtyTwoHistoricalTitlesAreCappedAndOmissionsCountDistinct()
    {
        var target = new Dictionary<string, WuaCachedDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        var list = Enumerable.Range(1, 45).Select(Hint).ToArray();
        var count = WuaCachedDriverHintCollector.Merge(target,
            list.Concat([Hint(35), Hint(36)]), [], out var omitted);
        Assert.Equal(WuaCachedDriverHintCollector.MaximumHints, count);
        Assert.Equal(13, omitted);
        Assert.Equal(32, target.Count);
    }

    [Theory]
    [InlineData("not-a-guid", "NVIDIA - Display")]
    [InlineData(null, "Intel - Net")]
    [InlineData("00000000-0000-4000-8000-000000000001", "")]
    [InlineData("00000000-0000-4000-8000-000000000001", "NVIDIA \nPRIVATE")]
    [InlineData("00000000-0000-4000-8000-000000000001", @"C:\Users\PRIVATE\driver")]
    [InlineData("00000000-0000-4000-8000-000000000001", "NVIDIA https://example.com/file")]
    [InlineData("00000000-0000-4000-8000-000000000001", "NVIDIA \u202EPRIVATE")]
    public void RejectsUnsafeHistoricalTitleOrGuid(string? id, string? title) =>
        Assert.Null(WuaCachedDriverHintCollector.TryCreate(id, title));

    [Fact]
    public void ExtraLongTitlesCannotEnterCacheReview()
    {
        Assert.Null(WuaCachedDriverHintCollector.TryCreate(
            Id(1), new string('X', 201)));
        Assert.NotNull(WuaCachedDriverHintCollector.TryCreate(
            Id(1), "Intel(R) Wireless Bluetooth(R) - 24.80.0.2"));
    }

    [Fact]
    public void CacheFailureDoesNotMakeHealthyOnlineDriverSearchPartial()
    {
        var report = new DriverSearchReport(
            [new DriverUpdateInfo(Id(1), "NVIDIA - Display", null,
                "Display", "NVIDIA", null, null, false,
                "NVIDIA", "RTX 4060", @"PCI\VEN_10DE&DEV_28A0", null)],
            [new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "")],
            CachedDriverSources: [
                new DriverSearchPassStatus(
                    WuaCachedDriverHintCollector.Source,
                    "Failed", 0, "PRIVATE PATH")
            ]);
        Assert.True(report.CachedDriverSearchFailed);
        Assert.False(report.IsPartial);
        Assert.False(report.AllFailed);
        Assert.Empty(report.CachedDriverReviewItems);
    }

    [Fact]
    public void OfflinePlanSeparatesInstalledFromNotInstalledWithoutOnlineQueries()
    {
        var passes = WuaCachedDriverSearchPlan.Create();
        Assert.Equal(2, passes.Count);
        Assert.False(passes[0].WasInstalled);
        Assert.True(passes[1].WasInstalled);
        Assert.Equal("IsInstalled=0 and Type='Driver'", passes[0].Criteria);
        Assert.Equal("IsInstalled=1 and Type='Driver'", passes[1].Criteria);
        Assert.All(passes, pass =>
        {
            Assert.DoesNotContain("Software", pass.Criteria);
            Assert.DoesNotContain("IsHidden", pass.Criteria);
        });
    }

    [Fact]
    public void BothCacheStatesGetFairShareOfThirtyTwoRowsAndTrueOmissions()
    {
        var notInstalled = Enumerable.Range(1, 40)
            .Select(index => Hint(index) with { WasInstalled = false }).ToArray();
        var installed = Enumerable.Range(101, 40)
            .Select(index => Hint(index) with { WasInstalled = true }).ToArray();
        var results = new Dictionary<string, WuaCachedDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        var added = WuaCachedDriverHintCollector.MergeBalanced(
            results, notInstalled, installed, [], out var omitted);

        Assert.Equal(32, added);
        Assert.Equal(48, omitted);
        Assert.Equal(16, results.Values.Count(hint => hint.WasInstalled == true));
        Assert.Equal(16, results.Values.Count(hint => hint.WasInstalled == false));
        Assert.All(results.Values, hint => Assert.False(hint.IsInstallable));
        Assert.All(results.Values, hint => Assert.False(hint.IsVerifiedAvailable));
        Assert.Contains("già installato", installed[0].Detail);
        Assert.Contains("non installato", notInstalled[0].Detail);
        Assert.Contains("disponibilità attuale", installed[0].Detail);
    }

    [Fact]
    public void ShortOrFailedOfflineSourceDoesNotWasteOrBlockPreviewSlots()
    {
        var results = new Dictionary<string, WuaCachedDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        var one = Hint(1) with { WasInstalled = false };
        var installed = Enumerable.Range(101, 40)
            .Select(i => Hint(i) with { WasInstalled = true }).ToArray();
        var added = WuaCachedDriverHintCollector.MergeBalanced(
            results, [one], installed, [], out var omitted);
        Assert.Equal(32, added);
        Assert.Equal(9, omitted);
        Assert.Equal(1, results.Values.Count(x => x.WasInstalled == false));
        Assert.Equal(31, results.Values.Count(x => x.WasInstalled == true));

        var confirmed = Hint(101).UpdateId;
        var onlyInstalled = new Dictionary<string, WuaCachedDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        WuaCachedDriverHintCollector.MergeBalanced(
            onlyInstalled, [], installed, [confirmed], out var skipped);
        Assert.False(onlyInstalled.ContainsKey(confirmed));
        Assert.Equal(32, onlyInstalled.Count);
        Assert.Equal(7, skipped);
    }

    [Fact]
    public void SameCachedGuidAcrossBothStatesIsKeptOnceAndOnlineOffersWin()
    {
        var onlineGuid = Hint(7).UpdateId;
        var repeated = Hint(5);
        var target = new Dictionary<string, WuaCachedDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        var added = WuaCachedDriverHintCollector.MergeBalanced(
            target,
            [Hint(7) with { WasInstalled = false },
             repeated with { WasInstalled = false }],
            [repeated with
            {
                UpdateId = repeated.UpdateId.ToUpperInvariant(),
                WasInstalled = true
            }, Hint(9) with { WasInstalled = true }],
            [onlineGuid],
            out var omitted);

        Assert.Equal(2, added);
        Assert.Equal(0, omitted);
        Assert.Equal(2, target.Count);
        Assert.False(target.ContainsKey(onlineGuid));
        Assert.Null(target[repeated.UpdateId].WasInstalled);
        Assert.True(target[Hint(9).UpdateId].WasInstalled);
        Assert.Contains("sconosciuto", target[repeated.UpdateId].Detail);
    }

    [Fact]
    public void OversizedOfflinePassCannotBypassRawCountCeiling()
    {
        var invalid = Enumerable.Repeat(Hint(1),
            WuaCachedDriverHintCollector.MaximumRawResultsPerPass + 1)
            .ToArray();
        Assert.Throws<InvalidDataException>(() =>
            WuaCachedDriverHintCollector.MergeBalanced(
                new Dictionary<string, WuaCachedDriverHint>(),
                invalid, [], [], out _));
    }

    [Fact]
    public void CachedGuidFormatsFromBothOfflinePassesShareOneAmbiguousIdentity()
    {
        var guid = Guid.Parse(Id(5));
        var notInstalled = Hint(5) with
        {
            UpdateId = guid.ToString("B"), WasInstalled = false
        };
        var installed = Hint(5) with
        {
            UpdateId = guid.ToString("P"), WasInstalled = true
        };
        var other = Hint(6) with
        {
            UpdateId = Guid.Parse(Id(6)).ToString("N"), WasInstalled = true
        };
        var destination = new Dictionary<string, WuaCachedDriverHint>(
            StringComparer.OrdinalIgnoreCase);

        var added = WuaCachedDriverHintCollector.MergeBalanced(
            destination, [notInstalled], [installed, other], [],
            out var omitted);

        Assert.Equal(2, added);
        Assert.Equal(0, omitted);
        Assert.Equal(2, destination.Count);
        Assert.Null(destination[Id(5)].WasInstalled);
        Assert.True(destination[Id(6)].WasInstalled);
        Assert.All(destination, entry =>
            Assert.Equal(Guid.Parse(entry.Key).ToString("D"), entry.Key));
    }

    [Fact]
    public void OnlineGuidInOtherSyntaxCannotConsumeOfflineReviewSlot()
    {
        var online = Guid.Parse(Id(10));
        var historical = Hint(10) with { UpdateId = online.ToString("P") };
        var destination = new Dictionary<string, WuaCachedDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        var offered = WuaCachedDriverHintCollector.Merge(destination,
            [historical, Hint(11)],
            [online.ToString("B")], out var omitted);

        Assert.Equal(1, offered);
        Assert.Equal(0, omitted);
        Assert.False(destination.ContainsKey(Id(10)));
        Assert.True(destination.ContainsKey(Id(11)));
    }

    [Fact]
    public void DifferentGuidSyntaxCannotInflateOfflineOverflowCount()
    {
        var initial = Enumerable.Range(1, 32).Select(Hint).ToArray();
        var one = Guid.Parse(Id(33));
        var destination = new Dictionary<string, WuaCachedDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        WuaCachedDriverHintCollector.Merge(destination, initial,
            [], out _);

        var added = WuaCachedDriverHintCollector.Merge(destination,
            [Hint(33) with { UpdateId = one.ToString("B") },
             Hint(33) with { UpdateId = one.ToString("P") },
             Hint(33) with { UpdateId = one.ToString("N") }],
            [], out var omitted);

        Assert.Equal(0, added);
        Assert.Equal(1, omitted);
        Assert.Equal(32, destination.Count);
    }

    [Fact]
    public void LegacyDestinationKeyAndMalformedCandidateCannotForgeCacheEntry()
    {
        var known = Guid.Parse(Id(1));
        var destination = new Dictionary<string, WuaCachedDriverHint>
        {
            [known.ToString("B")] = Hint(1)
        };
        var added = WuaCachedDriverHintCollector.Merge(destination,
            [Hint(1) with { UpdateId = known.ToString("N") },
             new WuaCachedDriverHint("PRIVATE", "NVIDIA Driver"),
             new WuaCachedDriverHint(Id(2), @"C:\Users\PRIVATE\driver"),
             Hint(3)], [], out var omitted);

        Assert.Equal(1, added);
        Assert.Equal(0, omitted);
        Assert.Equal(2, destination.Count);
        Assert.True(destination.ContainsKey(Id(3)));
        Assert.DoesNotContain(destination.Values, item =>
            item.Title.Contains("PRIVATE"));
    }

    [Fact]
    public void OldIpcJsonWithoutCacheFieldsStillRoundTrips()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 0, "")
        ], CachedDriverHints: [Hint(1)]);
        var json = JsonSerializer.Serialize(report);
        var old = JsonNode.Parse(json)!.AsObject();
        Assert.True(old.Remove(nameof(DriverSearchReport.CachedDriverHints)));
        Assert.True(old.Remove(nameof(DriverSearchReport.CachedDriverSources)));
        Assert.True(old.Remove(nameof(DriverSearchReport.CachedDriverHintsOmitted)));
        var restored = JsonSerializer.Deserialize<DriverSearchReport>(
            old.ToJsonString());
        Assert.NotNull(restored);
        Assert.Empty(restored.CachedDriverReviewItems);
        Assert.False(restored.CachedDriverSearchFailed);
        Assert.False(restored.IsPartial);
    }
}
