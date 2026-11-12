using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A completed WUA Type=Driver source may contribute results only once.
/// A discarded source must also make the overall scan visibly incomplete.
/// </summary>
public sealed class WuaSourceStatusIntegrityTests
{
    private const string GuidA = "00000000-0000-4000-8000-000000000382";
    private const string GuidB = "00000000-0000-4000-8000-000000000383";

    private static DriverUpdateInfo Update(string id, string source) =>
        new(id, "Generic Windows driver", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null,
            @"PCI\VEN_10DE&DEV_28A0", null, SearchSource: source);

    private static DriverSearchPassStatus Pass(
        string source, string outcome = "Completed", int count = 0) =>
        new(source, outcome, count, "PRIVATE_WSUS_ENDPOINT");

    private static SimpleScanPresentation Home(DriverSearchReport report) =>
        SimpleScanPresentation.Create(report, true, 8, 0, 0, []);

    [Fact]
    public void DuplicateCompletedSourceMakesEmptyHomeExplicitlyPartial()
    {
        var report = new DriverSearchReport([], [
            Pass("Windows configurato"),
            Pass("Windows Update online"),
            Pass("WINDOWS UPDATE ONLINE")
        ]);

        Assert.True(report.IsPartial);
        Assert.True(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report));
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
        var home = Home(report);
        Assert.Contains("Analisi parziale", home.Headline);
        Assert.Contains("0 candidati Windows Update", home.SourceLine);
        Assert.Contains("copertura PARZIALE", home.SourceLine);
        Assert.Contains("risultati possono essere incompleti", home.Warning);

        var message = CompactResultsEmptyMessage.Create(0, report);
        Assert.Contains("ricerca è incompleta", message);
        Assert.DoesNotContain("servizi Windows consultati", message);
        Assert.All(WuaPassStatusPresentation.Create(report), line =>
            Assert.DoesNotContain("PRIVATE", line));
    }

    [Theory]
    [InlineData("Completed", "Failed")]
    [InlineData("Completed", "Partial")]
    [InlineData("Completed", "NetworkError")]
    [InlineData("Completed", "Completed")]
    public void ContradictoryPassesNeverValidateTheirUpdate(
        string first, string second)
    {
        var report = new DriverSearchReport([
            Update(GuidA, "Windows configurato"),
            Update(GuidB, "Windows Update online")
        ], [
            Pass("Windows configurato", "Completed", 1),
            Pass("Windows Update online", first, 1),
            Pass("Windows Update online", second, 1)
        ]);

        Assert.True(report.IsPartial);
        var confirmed = Assert.Single(
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
        Assert.Equal(GuidA, confirmed.Id);

        var home = Home(report);
        Assert.StartsWith("Risultati parziali:", home.Headline);
        Assert.Contains("1 possibile aggiornamento", home.Headline);
        Assert.Contains("1 candidati Windows Update", home.SourceLine);
        Assert.DoesNotContain("2 possibili aggiornamenti", home.Headline);
        Assert.Contains("risultati possono essere incompleti", home.Warning);
    }

    [Fact]
    public void UnknownCompletedSourceCannotCertifySuccessfulScan()
    {
        var report = new DriverSearchReport([
            Update(GuidB, @"\\PRIVATE-WSUS\Sensitive")
        ], [
            Pass("Windows configurato"),
            Pass(@"\\PRIVATE-WSUS\Sensitive")
        ]);

        Assert.True(report.IsPartial);
        Assert.True(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report));
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
        Assert.Contains("Analisi parziale", Home(report).Headline);
        var visible = string.Join(" ", WuaPassStatusPresentation.Create(report));
        Assert.Contains("Fonte Windows non riconosciuta", visible);
        Assert.DoesNotContain("PRIVATE", visible);
        Assert.DoesNotContain("Sensitive", visible);
    }

    [Theory]
    [InlineData("Windows configurato")]
    [InlineData("Windows Update online")]
    [InlineData("Windows configurato · avanzato")]
    [InlineData("Microsoft Update online · avanzato")]
    public void NotRegisteredIsNeutralOnlyForMicrosoftUpdate(
        string source)
    {
        var report = new DriverSearchReport([], [
            Pass("Microsoft Update online"),
            Pass(source, "NotRegistered")
        ]);
        Assert.True(report.IsPartial);
        Assert.Contains("Analisi parziale", Home(report).Headline);
        Assert.Contains("ricerca è incompleta",
            CompactResultsEmptyMessage.Create(0, report));
    }

    [Fact]
    public void RegisteredSourcesAndOnlyMicrosoftNotRegisteredAreComplete()
    {
        var report = new DriverSearchReport([], [
            Pass("Windows configurato"),
            WindowsDriverSearchPlan.NotRegisteredStatus()
        ]);
        Assert.False(report.IsPartial);
        Assert.Equal("Nessun aggiornamento proposto dalle fonti consultate",
            Home(report).Headline);
        Assert.Contains("servizi Windows consultati",
            CompactResultsEmptyMessage.Create(0, report));

        var all = new DriverSearchReport([
            Update(GuidA, "Windows configurato"),
            Update(GuidB, "Windows Update online")
        ], [
            Pass("Windows configurato", count: 1),
            Pass("Windows Update online", count: 1),
            Pass("Microsoft Update online")
        ]);
        Assert.False(all.IsPartial);
        Assert.Equal(2,
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(all).Count);
        Assert.Equal("2 possibili aggiornamenti rilevati", Home(all).Headline);
    }

    [Fact]
    public void SoftwareAndCachedFailuresDoNotInvalidateTypeDriver()
    {
        var report = new DriverSearchReport([], [
            Pass("Windows configurato")
        ], RelatedSoftwareSources: [
            Pass("Windows Update online · componenti software", "Failed")
        ], CachedDriverSources: [
            Pass("Windows Update cache", "Failed")
        ]);
        Assert.False(report.IsPartial);
        Assert.True(report.SoftwareSearchIsPartial);
        Assert.True(report.CachedDriverSearchFailed);
        Assert.Contains("Ricerca driver Windows completata",
            Home(report).Warning);
    }

    [Fact]
    public void UnknownOnlySourceIsNeverPresentedAsCompletedScan()
    {
        var report = new DriverSearchReport([], [
            Pass("PRIVATE_OEM_VENDOR", "Completed")
        ]);
        Assert.False(
            WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report));
        Assert.True(report.IsPartial);
        Assert.Equal("Ricerca aggiornamenti non completata",
            Home(report).Headline);
        Assert.Contains("Non è stato possibile verificare",
            CompactResultsEmptyMessage.Create(0, report));
    }
}
