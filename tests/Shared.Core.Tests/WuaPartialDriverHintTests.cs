using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaPartialDriverHintTests
{
    private static WuaPartialDriverHint Hint(string id, string title,
        string source = "Windows Update online") =>
        Assert.IsType<WuaPartialDriverHint>(
            WuaPartialDriverHintCollector.TryCreate(id, title, source));

    [Theory]
    [InlineData("Windows configurato")]
    [InlineData("Windows Update online")]
    [InlineData("Windows configurato · avanzato")]
    [InlineData("Windows Update online · avanzato")]
    [InlineData("Microsoft Update online")]
    [InlineData("Microsoft Update online · avanzato")]
    public void OfficialDriverPassNamesCanProduceReadOnlyHints(string source)
    {
        var hint = Hint(Guid.NewGuid().ToString("D"),
            "NVIDIA - Display - 32.0.16.2000", source);
        Assert.False(hint.IsInstallable);
        Assert.False(hint.IsVerified);
        Assert.Contains("NON installabile", hint.Detail);
        Assert.Contains("ricerca WUA parziale", hint.Detail);
    }

    [Theory]
    [InlineData("ThirdPartyEasyDriver")]
    [InlineData("Windows Update online · componenti software")]
    [InlineData("Microsoft Update download")]
    [InlineData("")]
    public void NonDriverOrUnknownSourcesNeverBecomeHints(string source) =>
        Assert.Null(WuaPartialDriverHintCollector.TryCreate(
            Guid.NewGuid().ToString("D"), "NVIDIA - Display", source));

    [Theory]
    [InlineData("NVIDIA - Display - 32.0.1\nPRIVATE")]
    [InlineData("NVIDIA - Display: private")]
    [InlineData(@"C:\User\Private\driver.inf")]
    [InlineData("NVIDIA\u202e private")]
    [InlineData("Intel/PRIVATE")]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsUntrustedTitlesInsteadOfLeakingThem(string title) =>
        Assert.Null(WuaPartialDriverHintCollector.TryCreate(
            Guid.NewGuid().ToString("D"), title, "Windows configurato"));

    [Fact]
    public void RejectsInvalidGuidAndUnboundedTitle()
    {
        Assert.Null(WuaPartialDriverHintCollector.TryCreate(
            "fake-wua-guid", "Intel driver", "Windows configurato"));
        Assert.Null(WuaPartialDriverHintCollector.TryCreate(
            Guid.NewGuid().ToString("D"), new string('X', 201),
            "Windows configurato"));
    }

    [Fact]
    public void CrossPassDeduplicationAndMaximumAreStrict()
    {
        var output = new Dictionary<string, WuaPartialDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        var omittedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = Enumerable.Range(0, 39)
            .Select(_ => Guid.NewGuid().ToString("D")).ToArray();
        var batch = ids.Select((id, i) =>
            Hint(id, "Intel - Driver " + i)).ToArray();

        var first = WuaPartialDriverHintCollector.Merge(
            output, batch, out var skipped, omittedIds);
        Assert.Equal(32, first);
        Assert.Equal(32, output.Count);
        Assert.Equal(7, skipped);
        Assert.Equal(7, omittedIds.Count);

        var retry = batch.Select(row => row with
        {
            UpdateId = row.UpdateId.ToUpperInvariant(),
            SearchSource = "Microsoft Update online"
        }).ToArray();

        var second = WuaPartialDriverHintCollector.Merge(
            output, retry, out var retrySkipped, omittedIds);
        Assert.Equal(0, second);
        Assert.Equal(0, retrySkipped);
        Assert.Equal(32, output.Count);
    }

    [Fact]
    public void DuplicatesInsideSinglePassAreCountedOnlyOnce()
    {
        var id = Guid.NewGuid().ToString("D");
        var items = new[]
        {
            Hint(id, "NVIDIA - Display"),
            Hint(id.ToUpperInvariant(), "NVIDIA - Display duplicate")
        };
        var output = new Dictionary<string, WuaPartialDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WuaPartialDriverHintCollector.Merge(
            output, items, out var omitted));
        Assert.Equal(0, omitted);
        Assert.Single(output);
    }

    [Fact]
    public void PartialHintsNeverChangeVerifiedWuaCountsOrInstallation()
    {
        var hint = Hint(Guid.NewGuid().ToString("D"), "NVIDIA - Display");
        var report = new DriverSearchReport(
            [], [new DriverSearchPassStatus("Windows Update online",
                    "Partial", 1, "incomplete")],
            PartialDriverHints: [hint], PartialDriverHintsOmitted: 4);

        Assert.Empty(report.Updates);
        Assert.True(report.IsPartial);
        Assert.True(report.AllFailed);
        Assert.Single(report.PartialDriverReviewItems);
        Assert.Equal(4, report.PartialDriverHintsOmitted);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(report.Updates));
        Assert.False(hint.IsInstallable);

        var summary = ScanCoverageText.Create(report, [],
            deviceCount: 1, problemCount: 0, manufacturerRecommended: 0);
        Assert.Contains("1 visualizzati, 4 oltre limite", summary);
        Assert.Contains("NON installabili", summary);
        Assert.Contains("Windows Update: ricerca non verificata", summary);
        Assert.DoesNotContain(hint.Title, summary);
        Assert.DoesNotContain(hint.UpdateId, summary);
    }

    [Fact]
    public void CleanDriverOffersRemainIndependentOfPartialHints()
    {
        var guid = Guid.NewGuid().ToString("D");
        var clean = new DriverUpdateInfo(
            guid, "Intel - Net", null, "Net", "Intel", null,
            null, false, "Intel", null, @"PCI\VEN_8086&DEV_51F1", null,
            SearchSource: "Windows Update online");
        var hint = Hint(Guid.NewGuid().ToString("D"), "NVIDIA - Display");
        var report = new DriverSearchReport(
            [clean],
            [
                new("Windows configurato", "Partial", 1, "Incomplete"),
                new("Windows Update online", "Completed", 1, "OK")
            ],
            PartialDriverHints: [hint]);

        Assert.Single(report.Updates);
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(report.Updates));
        Assert.Single(report.PartialDriverReviewItems);
        Assert.True(report.IsPartial);
        Assert.False(report.AllFailed);
    }
}
