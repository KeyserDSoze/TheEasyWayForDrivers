using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Type=Software remains review-only, but every real UpdateID must consume
/// at most one of the 32 slots even across multiple WUA GUID spellings.
/// </summary>
public sealed class WuaSoftwareGuidFormatDedupTests
{
    private const string IntelId = "00000000-0000-4000-8000-000000000371";
    private const string NvidiaId = "00000000-0000-4000-8000-000000000372";
    private const string Source = "Windows Update online · componenti software";
    private const string Mu = "Microsoft Update online · componenti software";

    private static WuaDriverRelatedSoftware Hint(
        string id, string title = "Intel Wireless Extension",
        string source = Source) =>
        new(id, title, source);

    private static DriverSearchReport Report(
        IReadOnlyList<WuaDriverRelatedSoftware> hints) =>
        new([], [new DriverSearchPassStatus(
            "Windows Update online", "Completed", 0, "PRIVATE_SERVER")],
            hints,
            [new DriverSearchPassStatus(Source, "Completed", hints.Count,
                "PRIVATE_SOFTWARE_ENDPOINT")]);

    [Fact]
    public void TryCreateCanonicalizesAllFourValidGuidRepresentations()
    {
        var guid = Guid.Parse(IntelId);
        foreach (var spelling in new[]
        {
            guid.ToString("D"),
            guid.ToString("B").ToUpperInvariant(),
            guid.ToString("N"),
            guid.ToString("P").ToUpperInvariant()
        })
        {
            var item = WuaDriverRelatedSoftwareClassifier.TryCreate(
                spelling, "Intel Wireless Extension", Source);
            Assert.NotNull(item);
            Assert.Equal(IntelId, item.UpdateId);
            Assert.False(item.IsInstallable);
            Assert.False(item.IsDriverUpdate);
        }
        Assert.Null(WuaDriverRelatedSoftwareClassifier.TryCreate(
            "PRIVATE_SERIAL", "Intel Wireless Extension", Source));
    }

    [Fact]
    public void SelectFairDoesNotWasteSlotsOnFourFormsOfSameIntelGuid()
    {
        var intel = Guid.Parse(IntelId);
        var duplicate = new[]
        {
            Hint(intel.ToString("D")),
            Hint(intel.ToString("B")),
            Hint(intel.ToString("N")),
            Hint(intel.ToString("P"))
        };
        var others = Enumerable.Range(1, 95)
            .Select(i => Hint($"00000000-0000-4000-8000-{i:X12}"))
            .ToArray();
        var lateNvidia = Hint(NvidiaId, "NVIDIA Platform Controller", Mu);

        var result = WuaDriverRelatedSoftwareClassifier.SelectFair(
            duplicate.Concat(others).Append(lateNvidia)
                .Append(Hint("PRIVATE_NOT_GUID")).ToArray());
        Assert.Equal(32, result.Hints.Count);
        Assert.Equal(97 - 32, result.Omitted);
        Assert.Single(result.Hints, h => h.UpdateId == IntelId);
        Assert.Contains(result.Hints, h => h.UpdateId == NvidiaId);
        Assert.All(result.Hints, h =>
        {
            Assert.True(Guid.TryParseExact(h.UpdateId, "D", out _));
            Assert.False(h.IsInstallable);
        });
    }

    [Fact]
    public void MergeUsesGuidValueAcrossWuaSourcesAndLegacyDestination()
    {
        var guid = Guid.Parse(IntelId);
        var legacy = Hint(guid.ToString("B"));
        var destination = new Dictionary<string, WuaDriverRelatedSoftware>(
            StringComparer.OrdinalIgnoreCase)
        {
            [legacy.UpdateId] = legacy
        };
        var candidates = new[]
        {
            Hint(guid.ToString("D"), source: Mu),
            Hint(guid.ToString("N"), source: Source),
            Hint(guid.ToString("P"), source: Mu),
            Hint(NvidiaId, "NVIDIA Platform Controller", Mu)
        };
        Assert.Equal(1, WuaDriverRelatedSoftwareClassifier.Merge(
            destination, candidates, out var omitted));
        Assert.Equal(0, omitted);
        Assert.Equal(2, destination.Count);
        Assert.Single(destination.Where(pair =>
            Guid.Parse(pair.Key) == guid));
        Assert.Equal(NvidiaId, destination[NvidiaId].UpdateId);
        Assert.Equal(Mu, destination[NvidiaId].SearchSource);
    }

    [Fact]
    public void FullMergeUsesSingleCanonicalIdAndPreservesFirstProvenance()
    {
        var guid = Guid.Parse(IntelId);
        var first = Hint(guid.ToString("P"), source: Source);
        var second = Hint(guid.ToString("B"), source: Mu);
        var destination = new Dictionary<string, WuaDriverRelatedSoftware>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WuaDriverRelatedSoftwareClassifier.Merge(
            destination, [first, second, Hint("PRIVATE")], out var omitted));
        Assert.Equal(0, omitted);
        var row = Assert.Single(destination);
        Assert.Equal(IntelId, row.Key);
        Assert.Equal(IntelId, row.Value.UpdateId);
        Assert.Equal(Source, row.Value.SearchSource);
    }

    [Fact]
    public void OverflowIsCountedOnceAcrossGuidFormatsAndRepeatedPasses()
    {
        var destination = Enumerable.Range(1,
                WuaDriverRelatedSoftwareClassifier.MaxItems)
            .Select(i => Hint($"00000000-0000-4000-8000-{i:X12}"))
            .ToDictionary(x => x.UpdateId, x => x);
        var guid = Guid.Parse(NvidiaId);
        var overflow = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            guid.ToString("B")
        };
        Assert.Equal(0, WuaDriverRelatedSoftwareClassifier.Merge(
            destination, [Hint(guid.ToString("D"))], out var omitted,
            overflow));
        Assert.Equal(0, omitted);

        var other = Guid.NewGuid();
        Assert.Equal(0, WuaDriverRelatedSoftwareClassifier.Merge(
            destination,
            [Hint(other.ToString("B")), Hint(other.ToString("N"))],
            out var firstOmitted, overflow));
        Assert.Equal(1, firstOmitted);
        Assert.Contains(other.ToString("D"), overflow);

        Assert.Equal(0, WuaDriverRelatedSoftwareClassifier.Merge(
            destination, [Hint(other.ToString("P"))],
            out var repeatOmitted, overflow));
        Assert.Equal(0, repeatOmitted);
        Assert.Equal(32, destination.Count);
    }

    [Fact]
    public void EightCategoryAndCopyReportCountOneLegacyTypeSoftwareGuid()
    {
        var guid = Guid.Parse(IntelId);
        var report = Report(
        [
            Hint(guid.ToString("B"), "Intel PROSet Wireless Extension"),
            Hint(guid.ToString("N"), "Intel PROSet Wireless Extension", Mu),
            Hint(guid.ToString("P"), "Intel PROSet Wireless Extension")
        ]);
        var row = ReferenceDriverCoverage.Create([], report)[2];
        Assert.Equal(1, row.SoftwareTitleHints);
        Assert.Equal(0, row.WindowsDriverCandidates);
        Assert.True(row.Component.Contains("PROSet"));
        var copied = ScanCoverageText.Create(
            report, [], 0, 0, 0, [], inventoryComplete: true);
        Assert.Contains("1 titoli WUA Type=Software", copied);
        Assert.DoesNotContain("PRIVATE_SERVER", copied);
        Assert.DoesNotContain("PRIVATE_SOFTWARE_ENDPOINT", copied);
        Assert.DoesNotContain(IntelId, copied);
        Assert.DoesNotContain("00000000-0000", copied);
        Assert.False(row.SoftwareOnly);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds([]));
    }
}
