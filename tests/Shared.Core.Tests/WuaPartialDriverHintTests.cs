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

    [Fact]
    public void FairHintsRecoverLateNvidiaKillerAndRealtekAfterIntelFlood()
    {
        var staged = new Dictionary<string, WuaPartialDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        static WuaPartialDriverHint Make(int i, string title, string source) =>
            Hint($"00000000-0000-4000-8000-{i:D12}", title, source);

        var intel = Enumerable.Range(1, 80).Select(i =>
            Make(i, $"Intel - Net - {i}", "Windows configurato"));
        var nvidia = Enumerable.Range(81, 48).Select(i =>
            Make(i, $"NVIDIA - Display - {i}", "Windows Update online"));
        var killer = Enumerable.Range(129, 40).Select(i =>
            Make(i, $"Intel Killer - Net - {i}",
                "Microsoft Update online · avanzato"));
        var realtek = Enumerable.Range(169, 40).Select(i =>
            Make(i, $"Realtek - MEDIA - {i}",
                "Windows Update online · avanzato"));

        Assert.Equal(80, WuaPartialDriverHintCollector.MergeStaged(
            staged, intel));
        Assert.Equal(48, WuaPartialDriverHintCollector.MergeStaged(
            staged, nvidia));
        Assert.Equal(40, WuaPartialDriverHintCollector.MergeStaged(
            staged, killer));
        Assert.Equal(40, WuaPartialDriverHintCollector.MergeStaged(
            staged, realtek));

        var selected = WuaPartialDriverHintCollector.SelectFair(
            staged.Values.ToArray());
        Assert.Equal(32, selected.Hints.Count);
        Assert.Equal(176, selected.Omitted);
        Assert.All(new[] { "NVIDIA", "Killer", "Realtek", "Intel" },
            vendor => Assert.Contains(selected.Hints,
                h => h.Title.Contains(vendor,
                    StringComparison.OrdinalIgnoreCase)));
        Assert.True(selected.Hints.Count(h =>
            h.Title.StartsWith("NVIDIA", StringComparison.Ordinal)) >= 7);
        Assert.True(selected.Hints.Count(h =>
            h.Title.StartsWith("Realtek", StringComparison.Ordinal)) >= 7);
        Assert.All(selected.Hints, h =>
        {
            Assert.False(h.IsInstallable);
            Assert.False(h.IsVerified);
        });
    }

    [Fact]
    public void PartialHintSelectionFairlyRotatesTwoSourcesOfSameVendor()
    {
        var staged = new Dictionary<string, WuaPartialDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        var configured = Enumerable.Range(1, 60).Select(i => Hint(
            $"00000000-0000-4000-8000-{i:D12}",
            "Intel - Net " + i, "Windows configurato"));
        var online = Enumerable.Range(61, 60).Select(i => Hint(
            $"00000000-0000-4000-8000-{i:D12}",
            "Intel - Net " + i, "Windows Update online"));
        WuaPartialDriverHintCollector.MergeStaged(staged, configured);
        WuaPartialDriverHintCollector.MergeStaged(staged, online);
        var selected = WuaPartialDriverHintCollector.SelectFair(
            staged.Values.ToArray());
        Assert.Equal(32, selected.Hints.Count);
        Assert.Equal(88, selected.Omitted);
        Assert.Equal(16, selected.Hints.Count(h =>
            h.SearchSource == "Windows configurato"));
        Assert.Equal(16, selected.Hints.Count(h =>
            h.SearchSource == "Windows Update online"));
    }

    [Fact]
    public void ConfirmedWuaGuidIsExcludedBeforeCountingOmittedPartialHints()
    {
        const string confirmed = "00000000-0000-4000-8000-000000000999";
        var staged = new Dictionary<string, WuaPartialDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        var partials = new[]
        {
            Hint(confirmed, "NVIDIA - Display"),
            Hint(confirmed.ToUpperInvariant(), "NVIDIA - duplicate",
                "Microsoft Update online"),
            Hint("00000000-0000-4000-8000-000000000998",
                "Intel Killer - Net")
        };
        Assert.Equal(2, WuaPartialDriverHintCollector.MergeStaged(
            staged, partials));
        var result = WuaPartialDriverHintCollector.SelectFair(
            staged.Values.ToArray(), [confirmed]);
        Assert.Equal(0, result.Omitted);
        Assert.Contains("Killer", Assert.Single(result.Hints).Title);

        var report = new DriverSearchReport([],
            [new("Windows configurato", "Partial", 2, "PRIVATE")],
            PartialDriverHints: result.Hints);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(report.Updates));
        var summary = ScanCoverageText.Create(report, [], 1, 0, 0);
        Assert.DoesNotContain("NVIDIA - Display", summary);
        Assert.DoesNotContain("00000000-0000-4000", summary);
        Assert.DoesNotContain("PRIVATE", summary);
    }

    [Fact]
    public void PartialStagingLimitsAreAtomicAndUntrustedSourcesAreRejected()
    {
        var staged = new Dictionary<string, WuaPartialDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        for (var pass = 0; pass < 6; pass++)
        {
            var batch = Enumerable.Range(1, 4000).Select(i => Hint(
                $"00000000-0000-4000-8000-{pass * 4000 + i:D12}",
                "Intel - Net " + i, "Windows configurato"));
            Assert.Equal(4000, WuaPartialDriverHintCollector.MergeStaged(
                staged, batch));
        }
        Assert.Equal(WuaPartialDriverHintCollector.MaximumStagedHints,
            staged.Count);
        Assert.Throws<InvalidDataException>(() =>
            WuaPartialDriverHintCollector.MergeStaged(staged,
            [
                Hint("00000000-0000-4000-8000-000000024001",
                    "NVIDIA - Display")
            ]));
        Assert.Equal(WuaPartialDriverHintCollector.MaximumStagedHints,
            staged.Count);
        Assert.Throws<InvalidDataException>(() =>
            WuaPartialDriverHintCollector.SelectFair(
                staged.Values.Concat(staged.Values.Take(1)).ToArray()));
        var chosen = WuaPartialDriverHintCollector.SelectFair(
            staged.Values.ToArray());
        Assert.Equal(32, chosen.Hints.Count);
        Assert.Equal(23968, chosen.Omitted);

        var invalid = new WuaPartialDriverHint(
            "00000000-0000-4000-8000-000000024002",
            "PRIVATE_PATH\\x", "Fake OEM source");
        var other = new Dictionary<string, WuaPartialDriverHint>();
        Assert.Equal(0, WuaPartialDriverHintCollector.MergeStaged(
            other, [invalid]));
        Assert.Empty(other);
    }


    [Fact]
    public void SpoofedIntelTitlesCannotConsumeGenuineNvidiaKillerAndRealtekSlots()
    {
        // A partial WUA pass is NOT an install offer, but misleading
        // 32-slot preview budgets still hide relevant review-only clues.
        // Before #486, NVIDIAware/Killerware consumed the real vendors'
        // buckets even though all early rows were Intel network titles.
        var noise = Enumerable.Range(1, 150).Select(i =>
            Hint($"00000000-0000-4000-8000-{i:D12}",
                i % 3 switch
                {
                    0 => $"NVIDIAware Intel - Net - {i}",
                    1 => $"Killerware Intel - Net - {i}",
                    _ => $"NVIDIA\u0301ware Intel - Net - {i}"
                }, "Windows configurato")).ToArray();
        var actualNvidia = Hint("00000000-0000-4000-8000-000000009001",
            "NVIDIA - Display - 32.0", "Windows Update online");
        var actualKiller = Hint("00000000-0000-4000-8000-000000009002",
            "Intel Killer - Net", "Microsoft Update online · avanzato");
        var actualRealtek = Hint("00000000-0000-4000-8000-000000009003",
            "Realtek - MEDIA", "Windows Update online · avanzato");

        var fair = WuaPartialDriverHintCollector.SelectFair(
            [.. noise, actualNvidia, actualKiller, actualRealtek]);

        Assert.Equal(WuaPartialDriverHintCollector.MaximumHints,
            fair.Hints.Count);
        Assert.Equal(153 - WuaPartialDriverHintCollector.MaximumHints,
            fair.Omitted);
        Assert.Contains(fair.Hints, row => row.UpdateId == actualNvidia.UpdateId);
        Assert.Contains(fair.Hints, row => row.UpdateId == actualKiller.UpdateId);
        Assert.Contains(fair.Hints, row => row.UpdateId == actualRealtek.UpdateId);
        Assert.All(fair.Hints, hint =>
        {
            Assert.False(hint.IsVerified);
            Assert.False(hint.IsInstallable);
        });
    }

    [Fact]
    public void PunctuationSeparatedAmdAndLaterNvidiaKeepSeparatePartialSlots()
    {
        var spoofed = Enumerable.Range(1, 80).Select(i =>
            Hint($"00000000-0000-4000-8000-{i:D12}",
                $"NVIDIAware Intel - Net {i}", "Windows configurato")).ToArray();
        var amd = Hint("00000000-0000-4000-8000-000000009011",
            "AMD-Display", "Microsoft Update online");
        var nvidia = Hint("00000000-0000-4000-8000-000000009012",
            "NVIDIA - Display", "Windows Update online");
        var selected = WuaPartialDriverHintCollector.SelectFair(
            [.. spoofed, amd, nvidia]);
        Assert.Contains(selected.Hints, x => x.UpdateId == amd.UpdateId);
        Assert.Contains(selected.Hints, x => x.UpdateId == nvidia.UpdateId);
        Assert.All(selected.Hints, x =>
        {
            Assert.False(x.IsVerified);
            Assert.False(x.IsInstallable);
        });
    }

    [Fact]
    public void PartialPreviewNeverPromotesOtherSourceToConfirmedDriverOffer()
    {
        var actual = Hint("00000000-0000-4000-8000-000000009022",
            "NVIDIA - Display", "Windows Update online");
        var report = new DriverSearchReport(
            [], [new DriverSearchPassStatus(
                "Windows Update online", "Partial", 1, "partial")],
            PartialDriverHints: [actual]);
        var filtered = Assert.Single(
            WuaPartialDriverHintCollector.ConfirmedForReport(report));
        Assert.Equal(actual.UpdateId, filtered.UpdateId);
        Assert.False(filtered.IsVerified);
        Assert.False(filtered.IsInstallable);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedForReport(report));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(report.Updates));
    }

}
