using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// #515: WUA Type=Driver partial search reports *raw COM results*,
/// separately from safe review-only titles. No count is an install offer.
/// </summary>
public sealed class WuaPartialPassAccountingTests
{
    private const string Source = "Windows Update online";
    private static DriverSearchPassStatus Partial() =>
        new(Source, "Partial", 0, "PRIVATE_WUA_PROXY_PATH");

    private static string Id(int i) =>
        $"00000000-0000-4000-8000-{i:D12}";

    [Fact]
    public void FourComResultsAndThreeReadableTitlesRemainFourEnumerated()
    {
        var updates = new WuaPartialTitleGetterIsolationTests.FakeUpdates([
            new(Id(1), "NVIDIA - Display"),
            new(Id(2), "PRIVATE_TITLE", failTitle: true),
            new(Id(3), "Intel Killer - Wi-Fi"),
            new(Id(4), "Realtek - Audio")
        ]);
        var hints = WuaPartialResultReader.ReadAll(updates, 4, Source);
        var staged = new Dictionary<string, WuaPartialDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        var newlyStaged = WuaPartialDriverHintCollector.MergeStaged(
            staged, hints);
        var result = WuaPartialPassAccounting.Record(
            Partial(), rawEnumerated: 4,
            safeTitles: hints.Count, newlyStaged: newlyStaged);

        Assert.Equal(4, result.Candidates);
        Assert.Equal(0, result.NewUniqueCandidates);
        Assert.Equal("Partial", result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Contains("4 voci COM enumerate", result.Detail);
        Assert.Contains("3 titoli sicuri", result.Detail);
        Assert.Contains("1 voci senza indizio leggibile", result.Detail);
        Assert.Contains("3 nuovi indizi non verificati", result.Detail);
        Assert.Contains("NON indicano driver disponibili o installabili",
            result.Detail);
        Assert.Equal(4, updates.Calls);
        Assert.Equal(3, staged.Count);
        Assert.All(staged.Values, h =>
        {
            Assert.False(h.IsInstallable);
            Assert.False(h.IsVerified);
        });
        Assert.DoesNotContain("PRIVATE_", result.Detail);

        var report = new DriverSearchReport([], [result],
            PartialDriverHints: staged.Values.ToArray());
        Assert.Equal(3, WuaPartialDriverHintCollector
            .ConfirmedForReport(report).Count);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedForReport(report));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(report.Updates));
    }

    [Fact]
    public void DuplicateOrInvalidTitlesAreNotMiscountedAsRawResultsLost()
    {
        var updates = new WuaPartialTitleGetterIsolationTests.FakeUpdates([
            new(Id(5), "NVIDIA - Display"),
            new(Id(5), "NVIDIA - Display duplicate"),
            new("not-a-guid", "Other - Driver"),
            new(Id(6), 12345),
            new(Id(7), "Intel Killer")
        ]);
        var hints = WuaPartialResultReader.ReadAll(updates, 5, Source);
        Assert.Equal(3, hints.Count);
        // Valid GUID/titles can repeat in WUA; only distinct review hints
        // are staged, while the source enumerated count stays five.
        var staged = new Dictionary<string, WuaPartialDriverHint>();
        var added = WuaPartialDriverHintCollector.MergeStaged(staged, hints);
        var result = WuaPartialPassAccounting.Record(
            Partial(), 5, hints.Count, added);
        Assert.Equal(5, result.Candidates);
        Assert.Equal(2, staged.Count);
        Assert.Equal(0, result.NewUniqueCandidates);
        Assert.Contains("5 voci COM enumerate", result.Detail);
        Assert.Contains("2 nuovi indizi", result.Detail);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(4, 0, 0)]
    [InlineData(4, 3, 0)]
    [InlineData(4000, 3999, 3500)]
    public void BoundedPartialAccountingStaysDistinctFromConfirmedGuidCounts(
        int raw, int safe, int newlyStaged)
    {
        var result = WuaPartialPassAccounting.Record(
            Partial(), raw, safe, newlyStaged);
        Assert.Equal(raw, result.Candidates);
        Assert.Equal(0, result.NewUniqueCandidates);
        Assert.False(result.Succeeded);
        Assert.Contains($"{raw - safe} voci senza indizio leggibile",
            result.Detail);
        Assert.DoesNotContain("PRIVATE_WUA_PROXY_PATH", result.Detail);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(4001, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(4, -1, 0)]
    [InlineData(4, 5, 0)]
    [InlineData(4, 2, 3)]
    [InlineData(1, 1, -1)]
    public void InvalidOrOverBudgetCountersFailClosed(
        int raw, int safe, int added)
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            WuaPartialPassAccounting.Record(Partial(), raw, safe, added));
        Assert.DoesNotContain("PRIVATE_", error.Message);
    }

    [Fact]
    public void CompletedAndFailedPassesCannotBeLaunderedAsPartial()
    {
        Assert.Throws<InvalidDataException>(() =>
            WuaPartialPassAccounting.Record(
                Partial() with { Outcome = "Completed" }, 1, 1, 1));
        Assert.Throws<InvalidDataException>(() =>
            WuaPartialPassAccounting.Record(
                Partial() with { Outcome = "Failed" }, 1, 0, 0));
        Assert.Throws<InvalidDataException>(() =>
            WuaPartialPassAccounting.Record(
                Partial() with { NewUniqueCandidates = 1 }, 1, 1, 1));
        Assert.Throws<ArgumentNullException>(() =>
            WuaPartialPassAccounting.Record(null!, 1, 0, 0));
    }

    [Fact]
    public void FailedStructuralEnumerationNeverProducesAnAccountingResult()
    {
        var updates = new WuaPartialTitleGetterIsolationTests.FakeUpdates([
            new(Id(1), "NVIDIA - Display"),
            new(Id(2), "Intel Killer"),
            new(Id(3), "Realtek - Audio")
        ], failAt: 1);
        Assert.ThrowsAny<Exception>(() =>
            WuaPartialResultReader.ReadAll(updates, 3, Source));
        // The production caller only invokes Record after ReadAll
        // completes; failure returns no list to commit.
    }
}
