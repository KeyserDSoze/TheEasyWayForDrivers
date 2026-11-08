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
            new DriverSearchPassStatus(WuaCachedDriverHintCollector.Source,
                "Completed", 1, "")
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
