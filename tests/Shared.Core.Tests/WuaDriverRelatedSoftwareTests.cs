using System.Text.Json;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaDriverRelatedSoftwareTests
{
    private const string Id = "b7c37b92-e513-4a1a-a812-86756d53e9b4";
    private const string Source = "Windows Update online · componenti software";

    [Theory]
    [InlineData("Intel® PROSet/Wireless WiFi Software extension")]
    [InlineData("Killer Networking Software - 50.26.915.2621")]
    [InlineData("NVIDIA - SoftwareComponent - 32.0.16.2101")]
    [InlineData("NVIDIA HD Audio Platform Controller")]
    [InlineData("Intel(R) Wireless Bluetooth(R)")]
    [InlineData("Realtek Semiconductor Corp. - MEDIA - Audio Driver")]
    [InlineData("Advanced Micro Devices - Display Driver")]
    public void IdentifiesVendorSoftwareOnlyAsInformationalEvidence(string title)
    {
        var software = WuaDriverRelatedSoftwareClassifier.TryCreate(
            Id, title, Source);
        Assert.NotNull(software);
        Assert.False(software.IsDriverUpdate);
        Assert.False(software.IsInstallable);
        Assert.Contains("NON driver", software.Detail);
        Assert.Equal(title, software.Title);
    }

    [Theory]
    [InlineData("2026-11 Cumulative Update for Windows 11")]
    [InlineData("Microsoft Defender Antivirus Security Intelligence")]
    [InlineData("Microsoft Office Update")]
    [InlineData("Intel Management Engine Firmware")]
    [InlineData("NVIDIA BIOS Update")]
    [InlineData("Killer Networking UEFI Firmware")]
    [InlineData("NVIDIA GeForce Experience")]
    [InlineData("Intel(R) System Update")]
    [InlineData("7-Zip File Manager")]
    public void DoesNotInventDriverCompanionsFromArbitraryUpdates(string title)
    {
        Assert.Null(WuaDriverRelatedSoftwareClassifier.TryCreate(
            Id, title, Source));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-guid")]
    [InlineData("123")]
    public void RejectsNonGuidIdentity(string id) =>
        Assert.Null(WuaDriverRelatedSoftwareClassifier.TryCreate(
            id, "Intel Wireless Bluetooth extension", Source));

    [Theory]
    [InlineData("Intel Wireless Bluetooth extension\n")]
    [InlineData("Intel Wireless Bluetooth extension\\private")]
    [InlineData("Intel Wireless Bluetooth extension https://private.example/")]
    [InlineData("Intel Wireless Bluetooth extension\u202E")]
    public void RejectsSuspiciousOrPrivateWuaTitles(string title) =>
        Assert.Null(WuaDriverRelatedSoftwareClassifier.TryCreate(
            Id, title, Source));

    [Fact]
    public void UnknownSourcesCannotMasqueradeAsWuaSoftware()
    {
        Assert.Null(WuaDriverRelatedSoftwareClassifier.TryCreate(
            Id, "Intel Wireless Bluetooth", "OEM archive"));
        Assert.Null(WuaDriverRelatedSoftwareClassifier.TryCreate(
            Id, "Intel Wireless Bluetooth", "Windows Update online"));
    }

    [Fact]
    public void SameSoftwareGuidAcrossCatalogsIsOnlyOneReviewHint()
    {
        var first = WuaDriverRelatedSoftwareClassifier.TryCreate(
            Id, "Killer Networking Software", Source)!;
        var duplicate = first with
        {
            SearchSource = "Microsoft Update online · componenti software"
        };
        var destination = new Dictionary<string, WuaDriverRelatedSoftware>(
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, WuaDriverRelatedSoftwareClassifier.Merge(
            destination, [first, duplicate], out var omitted));
        Assert.Equal(0, omitted);
        Assert.Single(destination);
        Assert.Equal(Source, destination[Id].SearchSource);
    }

    [Fact]
    public void The32ItemCapIsExplicitAndNeverMutatesTheDriverList()
    {
        var rows = Enumerable.Range(0, WuaDriverRelatedSoftwareClassifier.MaxItems + 3)
            .Select(i => new WuaDriverRelatedSoftware(
                Guid.NewGuid().ToString(), "Intel Wireless extension",
                Source))
            .ToArray();
        var destination = new Dictionary<string, WuaDriverRelatedSoftware>();
        var count = WuaDriverRelatedSoftwareClassifier.Merge(
            destination, rows, out var omitted);
        Assert.Equal(WuaDriverRelatedSoftwareClassifier.MaxItems, count);
        Assert.Equal(3, omitted);
        Assert.Equal(WuaDriverRelatedSoftwareClassifier.MaxItems, destination.Count);
    }

    [Fact]
    public void RepeatedOverflowItemAcrossMultipleServicesCountsOnce()
    {
        var destination = Enumerable.Range(
                0, WuaDriverRelatedSoftwareClassifier.MaxItems)
            .Select(i => new WuaDriverRelatedSoftware(
                Guid.NewGuid().ToString(), "Intel Wireless extension", Source))
            .ToDictionary(item => item.UpdateId, item => item);
        var id = Guid.NewGuid().ToString();
        var extra = new WuaDriverRelatedSoftware(
            id, "Killer Networking Software", Source);
        var omittedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(0, WuaDriverRelatedSoftwareClassifier.Merge(
            destination, [extra, extra], out var firstOmitted, omittedIds));
        Assert.Equal(1, firstOmitted);
        Assert.Equal(0, WuaDriverRelatedSoftwareClassifier.Merge(
            destination, [extra with
            {
                SearchSource = "Microsoft Update online · componenti software"
            }], out var repeatedOmitted, omittedIds));
        Assert.Equal(0, repeatedOmitted);
        Assert.Single(omittedIds);
        Assert.Equal(WuaDriverRelatedSoftwareClassifier.MaxItems,
            destination.Count);
    }


    private static WuaDriverRelatedSoftware Hint(int id, string title,
        string source = Source) =>
        WuaDriverRelatedSoftwareClassifier.TryCreate(
            $"00000000-0000-4000-8000-{id:X12}", title, source)!;

    [Fact]
    public void FairSelectionKeepsLateNvidiaKillerAndAllMajorVendorsAcrossSources()
    {
        var earlyIntel = Enumerable.Range(1, 120)
            .Select(i => Hint(i, "Intel PROSet Wireless extension",
                "Windows configurato · componenti software")).ToArray();
        var later = new[]
        {
            Hint(200, "NVIDIA Platform Controller - SoftwareComponent"),
            Hint(201, "Killer Networking Software"),
            Hint(202, "Realtek Audio Driver"),
            Hint(203, "Advanced Micro Devices - Display Driver"),
            Hint(204, "Qualcomm Wireless Driver"),
            Hint(205, "Broadcom Network Driver"),
            Hint(206, "MediaTek Wireless Driver")
        }.Select(item => item with
        {
            SearchSource = "Microsoft Update online · componenti software"
        }).ToArray();
        var candidates = earlyIntel.Concat(later).ToArray();
        var result = WuaDriverRelatedSoftwareClassifier.SelectFair(candidates);

        Assert.Equal(WuaDriverRelatedSoftwareClassifier.MaxItems, result.Hints.Count);
        Assert.Equal(candidates.Length - 32, result.Omitted);
        Assert.All(later, item =>
            Assert.Contains(result.Hints, selected =>
                selected.UpdateId == item.UpdateId));
        Assert.All(result.Hints, item =>
        {
            Assert.False(item.IsDriverUpdate);
            Assert.False(item.IsInstallable);
        });

        var report = new DriverSearchReport([], [new(
            "Windows Update online", "Completed", 0, "No drivers")],
            result.Hints);
        Assert.Empty(report.Updates);
        var baseline = TheEasyWayForDrivers.Core.Drivers.ReferenceDriverCoverage
            .Create([], report);
        Assert.Equal(25, baseline[2].SoftwareTitleHints); // remaining Intel PROSet
        Assert.Equal(25, result.Hints.Count(item =>
            item.SearchSource == "Windows configurato · componenti software"));
        Assert.Equal(7, result.Hints.Count(item =>
            item.SearchSource == "Microsoft Update online · componenti software"));
        Assert.Equal(1, baseline[3].SoftwareTitleHints); // Killer
        Assert.Equal(1, baseline[7].SoftwareTitleHints); // NVIDIA Platform
    }

    [Fact]
    public void FairSelectionPreservesSmallInputOrderAndCrossServiceGuidDedup()
    {
        var first = Hint(1, "Intel Wireless extension");
        var second = Hint(2, "NVIDIA Audio Driver");
        var repeated = first with
        {
            SearchSource = "Microsoft Update online · componenti software"
        };
        var result = WuaDriverRelatedSoftwareClassifier.SelectFair(
            [first, second, repeated]);
        Assert.Equal([first, second], result.Hints);
        Assert.Equal(0, result.Omitted);
        Assert.Equal(Source, result.Hints[0].SearchSource);
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.SelectFair([]).Hints);
    }

    [Fact]
    public void FairSelectionDoesNotWasteSlotsOnSingleVendorAndAtExactly32()
    {
        var mono = Enumerable.Range(1, 90)
            .Select(i => Hint(i, "Intel Wireless extension")).ToArray();
        var selected = WuaDriverRelatedSoftwareClassifier.SelectFair(mono);
        Assert.Equal(mono.Take(32), selected.Hints);
        Assert.Equal(58, selected.Omitted);
        var exact = WuaDriverRelatedSoftwareClassifier.SelectFair(mono.Take(32).ToArray());
        Assert.Equal(mono.Take(32), exact.Hints);
        Assert.Equal(0, exact.Omitted);
    }

    [Fact]
    public void FairSelectionRejectsOversizedStagingBeforeConsumingIt()
    {
        var tooMany = Enumerable.Range(1,
            WuaDriverRelatedSoftwareClassifier.MaximumStagedItems + 1)
            .Select(i => Hint(i, "Intel Wireless extension")).ToArray();
        Assert.Throws<InvalidDataException>(() =>
            WuaDriverRelatedSoftwareClassifier.SelectFair(tooMany));
    }

    [Fact]
    public void RecommendedSearchDoesNotAddSoftwarePasses()
    {
        Assert.Empty(WindowsDriverSearchPlan.CreateRelatedSoftware(
            DriverSearchMode.Recommended, microsoftUpdateRegistered: true));
    }

    [Fact]
    public void ComprehensiveSearchOnlyUsesSupportedReadOnlyWuaSources()
    {
        var withoutMu = WindowsDriverSearchPlan.CreateRelatedSoftware(
            DriverSearchMode.Comprehensive, microsoftUpdateRegistered: false);
        Assert.Equal(2, withoutMu.Count);
        Assert.All(withoutMu, pass =>
        {
            Assert.Equal("IsInstalled=0 and IsHidden=0 and Type='Software'",
                pass.Criteria);
            Assert.False(pass.IncludePotentiallySuperseded);
            Assert.True(pass.IsAdvancedCandidate);
        });
        Assert.DoesNotContain(withoutMu, pass => pass.ServiceId is not null);

        var withMu = WindowsDriverSearchPlan.CreateRelatedSoftware(
            DriverSearchMode.Comprehensive, microsoftUpdateRegistered: true);
        Assert.Equal(3, withMu.Count);
        Assert.Equal(WindowsDriverSearchPlan.MicrosoftUpdateServiceId,
            withMu[2].ServiceId);
        Assert.Equal(3, withMu[2].ServerSelection);
    }

    [Fact]
    public void SoftwareHintsSurviveIpcRoundTripButNeverAffectDriverCount()
    {
        var hint = new WuaDriverRelatedSoftware(
            Id, "Killer Networking Software", Source);
        var report = new DriverSearchReport(
            [], [new DriverSearchPassStatus(
                "Windows Update online", "Completed", 0, "no drivers")],
            [hint],
            [new DriverSearchPassStatus(
                Source, "Completed", 1, "one review item")]);

        var restored = JsonSerializer.Deserialize<DriverSearchReport>(
            JsonSerializer.Serialize(report));
        Assert.NotNull(restored);
        Assert.Empty(restored.Updates);
        Assert.Single(restored.SoftwareReviewItems);
        Assert.False(restored.AllFailed);
        Assert.False(restored.IsPartial);
        Assert.False(restored.SoftwareReviewItems[0].IsInstallable);
    }

    [Fact]
    public void FailedSoftwareQueryIsInformationalWithoutPoisoningDriverStatus()
    {
        var report = new DriverSearchReport(
            [], [new DriverSearchPassStatus(
                "WU", "Completed", 0, "no drivers")],
            [], [new DriverSearchPassStatus(
                Source, "Failed", 0, "unknown")]);
        Assert.False(report.IsPartial);
        Assert.True(report.SoftwareSearchIsPartial);
        Assert.False(report.AllFailed);
        Assert.Empty(report.SoftwareReviewItems);
    }

    [Fact]
    public void ExistingDriverCandidatesNeverChangeWhenSoftwareAppears()
    {
        var driver = new DriverUpdateInfo(
            Id, "Intel Graphics driver", null, "Display", "Intel",
            null, null, false, "Intel", "Graphics",
            @"PCI\VEN_8086&DEV_46A6", null);
        var software = new WuaDriverRelatedSoftware(
            "cb6aa61e-8739-433e-b513-00d72358bcf9",
            "Killer Networking Software", Source);
        var report = new DriverSearchReport(
            [driver], [new DriverSearchPassStatus(
                "Windows configurato", "Completed", 1, "found")], [software]);
        Assert.Single(report.Updates);
        Assert.Single(report.SoftwareReviewItems);
        Assert.DoesNotContain(report.SoftwareReviewItems, hint =>
            hint.IsInstallable || hint.IsDriverUpdate);
    }
}
