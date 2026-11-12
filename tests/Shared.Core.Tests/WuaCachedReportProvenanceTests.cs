using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #416: offline WUA historical title review is not evidence from a
/// completed COM query unless its own verified cache source exists.
/// </summary>
public sealed class WuaCachedReportProvenanceTests
{
    private const string Uninstalled = "Cache Windows locale · non installati";
    private const string Installed = "Cache Windows locale · già installati";
    private const string IdA = "00000000-0000-4000-8000-000000000416";
    private const string IdB = "00000000-0000-4000-8000-000000000417";

    private static DriverSearchPassStatus Pass(
        string source, string outcome = "Completed", int count = 1,
        int newUnique = 0) =>
        new(source, outcome, count, "PRIVATE_CACHE_PATH", newUnique);

    private static WuaCachedDriverHint Hint(string id = IdA,
        bool? installed = false, string title = "NVIDIA Display Driver") =>
        new(id, title, installed);

    private static DriverSearchReport Report(
        IReadOnlyList<WuaCachedDriverHint> hints,
        params DriverSearchPassStatus[] offline) =>
        new([], [Pass("Windows configurato")],
            CachedDriverHints: hints, CachedDriverSources: offline);

    [Fact]
    public void OnlyOwnCompletedOfflineSourceValidatesHistoricalTitle()
    {
        var report = Report([Hint(), Hint(IdB, true)],
            Pass(Uninstalled), Pass(Installed, "NetworkError", 0));
        var trusted = Assert.Single(WuaCachedDriverHintCollector
            .ConfirmedForReport(report));
        Assert.Equal(IdA, trusted.UpdateId);
        Assert.False(trusted.WasInstalled);
        Assert.False(trusted.IsInstallable);
        Assert.True(report.CachedDriverSearchFailed);

        var text = ScanCoverageText.Create(report, [], 1, 0, 0);
        Assert.Contains("1 titoli distinti", text);
        Assert.Contains("Fonti offline parziali/non verificate", text);
        Assert.DoesNotContain("PRIVATE", text);
    }

    [Fact]
    public void TwoCompletedOfflineQueriesPreserveInstalledAndUnknownStates()
    {
        var report = Report([Hint(), Hint(IdB, true),
            Hint("00000000-0000-4000-8000-000000000418", null)],
            Pass(Uninstalled, count: 2), Pass(Installed, count: 2));

        var trusted = WuaCachedDriverHintCollector.ConfirmedForReport(report);
        Assert.Equal(3, trusted.Count);
        Assert.Contains(trusted, x => x.WasInstalled is null);
        Assert.Contains(trusted, x => x.WasInstalled is true);
        Assert.Contains(trusted, x => x.WasInstalled is false);
        Assert.False(report.CachedDriverSearchFailed);
        Assert.All(trusted, x => Assert.False(x.IsVerifiedAvailable));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Partial")]
    [InlineData("NetworkError")]
    public void FailedQueryCannotValidateUnknownOrItsState(string outcome)
    {
        var report = Report([Hint(installed: null), Hint(IdB, true)],
            Pass(Uninstalled), Pass(Installed, outcome, 0));
        Assert.Empty(WuaCachedDriverHintCollector.ConfirmedForReport(report));
        Assert.True(report.CachedDriverSearchFailed);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    public void ImpossibleCompletedCountersCannotAuthenticateCache(
        int candidates, int unique)
    {
        var report = Report([Hint()],
            Pass(Uninstalled, count: candidates, newUnique: unique),
            Pass(Installed, count: 0));
        Assert.Empty(WuaCachedDriverHintCollector.ConfirmedForReport(report));
        Assert.True(report.CachedDriverSearchFailed);
        var copied = ScanCoverageText.Create(report, [], 0, 0, 0);
        // The OTHER offline pass completed, but returned zero rows.
        // Its completion is valid; it cannot authenticate the hints
        // attributed to the source with impossible counters.
        Assert.Contains("0 titoli distinti", copied);
        Assert.Contains("Fonti offline parziali/non verificate", copied);
        Assert.DoesNotContain("1 titoli distinti", copied);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    public void DuplicateCacheStatusesNeverValidateEvenIfOneSucceeded(
        string secondOutcome)
    {
        var report = Report([Hint()],
            Pass(Uninstalled), Pass(Uninstalled, secondOutcome));
        Assert.Empty(WuaCachedDriverHintCollector.ConfirmedForReport(report));
        Assert.True(report.CachedDriverSearchFailed);
    }

    [Fact]
    public void SpoofedCompletedLegacySourceNeverAuthenticatesTitles()
    {
        var report = Report([Hint()],
            Pass(WuaCachedDriverHintCollector.Source), Pass(Installed, "Failed", 0));
        Assert.Empty(WuaCachedDriverHintCollector.ConfirmedForReport(report));
        Assert.True(report.CachedDriverSearchFailed);
        var copied = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.DoesNotContain("1 titoli distinti", copied);
        Assert.DoesNotContain("PRIVATE", copied);
    }

    [Fact]
    public void LegacyTitlesAreRescrubbedAndGuidFormatsDeduplicated()
    {
        var report = Report([
            Hint(title: @"NVIDIA Driver C:\Users\PRIVATE_NAME"),
            Hint(title: "NVIDIA Display Driver"),
            Hint("{" + IdA + "}", title: "NVIDIA Display Driver"),
            Hint(IdB, true, title: "Intel Bluetooth Driver")
        ], Pass(Uninstalled, count: 3), Pass(Installed, count: 1));

        var verified = WuaCachedDriverHintCollector.ConfirmedForReport(report);
        Assert.Equal(2, verified.Count);
        Assert.Contains(verified, x => x.UpdateId == IdA && x.WasInstalled == false);
        Assert.Contains(verified, x => x.UpdateId == IdB && x.WasInstalled == true);
        Assert.DoesNotContain(verified, x => x.Title.Contains("PRIVATE"));
    }

    [Theory]
    [InlineData("B")]
    [InlineData("N")]
    [InlineData("P")]
    public void OnlineCompletedGuidIsExcludedFromHistoricalReview(string format)
    {
        var online = new DriverUpdateInfo(IdA, "NVIDIA display driver",
            null, "Display", "NVIDIA", null, null, false,
            "NVIDIA", null, @"PCI\VEN_10DE&DEV_28A0", null,
            SearchSource: "Windows Update online");
        var report = new DriverSearchReport([online],
            [Pass("Windows Update online", count: 1)],
            CachedDriverHints: [Hint(Guid.Parse(IdA).ToString(format)),
                Hint(IdB, true)],
            CachedDriverSources: [Pass(Uninstalled), Pass(Installed)]);

        var historical = Assert.Single(WuaCachedDriverHintCollector
            .ConfirmedForReport(report));
        Assert.Equal(IdB, historical.UpdateId);
        Assert.True(historical.WasInstalled);
        Assert.DoesNotContain(IdA, historical.UpdateId);
        Assert.False(historical.IsInstallable);

        var copied = ScanCoverageText.Create(report, [], 2, 0, 0);
        Assert.Contains("1 titoli distinti", copied);
        Assert.DoesNotContain("2 titoli distinti", copied);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Partial")]
    [InlineData("NetworkError")]
    public void FailedOnlineSourceCannotSuppressValidHistoricalGuid(
        string outcome)
    {
        var online = new DriverUpdateInfo(IdA, "NVIDIA display driver",
            null, "Display", "NVIDIA", null, null, false,
            "NVIDIA", null, @"PCI\VEN_10DE&DEV_28A0", null,
            SearchSource: "Windows Update online");
        var report = new DriverSearchReport([online],
            [Pass("Windows configurato", count: 0),
             Pass("Windows Update online", outcome, count: 0)],
            CachedDriverHints: [Hint(Guid.Parse(IdA).ToString("P"))],
            CachedDriverSources: [Pass(Uninstalled), Pass(Installed)]);

        var historical = Assert.Single(WuaCachedDriverHintCollector
            .ConfirmedForReport(report));
        Assert.Equal(IdA, historical.UpdateId);
        Assert.False(historical.IsInstallable);
        Assert.True(report.IsPartial);
    }

    [Fact]
    public void UnknownOnlineSourceCannotSuppressUnrelatedHistoricalData()
    {
        var forgedOnline = new DriverUpdateInfo(IdA, "Fake title",
            null, "Display", "NVIDIA", null, null, false,
            "NVIDIA", null, @"PCI\VEN_10DE&DEV_28A0", null,
            SearchSource: "PRIVATE_FAKE_OEM");
        var report = new DriverSearchReport([forgedOnline],
            [Pass("Windows configurato", count: 0)],
            CachedDriverHints: [Hint(IdA)],
            CachedDriverSources: [Pass(Uninstalled), Pass(Installed)]);

        var historical = Assert.Single(WuaCachedDriverHintCollector
            .ConfirmedForReport(report));
        Assert.Equal(IdA, historical.UpdateId);
        Assert.DoesNotContain("PRIVATE", historical.Title);
    }

    [Fact]
    public void OversizedLegacyReviewAndUnverifiedOmissionsFailClosed()
    {
        var hints = Enumerable.Repeat(Hint(), 33).ToArray();
        var report = new DriverSearchReport([], [Pass("Windows configurato")],
            CachedDriverHints: hints,
            CachedDriverSources: [Pass(Uninstalled), Pass(Installed, "Failed", 0)],
            CachedDriverHintsOmitted: 100);
        Assert.Empty(WuaCachedDriverHintCollector.ConfirmedForReport(report));
        Assert.Equal(0, WuaCachedDriverHintCollector.VerifiedOmittedForReport(report));

        var complete = new DriverSearchReport([], [Pass("Windows configurato")],
            CachedDriverSources: [Pass(Uninstalled), Pass(Installed)],
            CachedDriverHintsOmitted: 3);
        Assert.Equal(3, WuaCachedDriverHintCollector.VerifiedOmittedForReport(complete));
    }
}
