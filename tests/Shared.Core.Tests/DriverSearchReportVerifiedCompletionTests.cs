using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #443: the scan-wide AllFailed status must agree with the completed,
/// authenticated Type=Driver WUA sources that authorize read-only evidence.
/// No fabricated "Completed" can downgrade a fully unverified scan.
/// </summary>
public sealed class DriverSearchReportVerifiedCompletionTests
{
    private static DriverSearchPassStatus Status(
        string source, string outcome = "Completed",
        int total = 0, int unique = 0) =>
        new(source, outcome, total, "PRIVATE_WSUS_SERIAL",
            NewUniqueCandidates: unique);

    private static DriverSearchReport Report(
        params DriverSearchPassStatus[] sources) => new([], sources);

    [Theory]
    [InlineData("PRIVATE_VENDOR")]
    [InlineData("Windows Update online · componenti software")]
    [InlineData("Windows Update cache")]
    [InlineData("Windows Update")]
    public void ForeignCompletedCannotDisguiseNoVerifiedDriverSearch(string source)
    {
        var report = Report(Status(source), Status(
            "Windows configurato", "Failed"));

        Assert.True(report.AllFailed);
        Assert.True(report.IsPartial);
        Assert.False(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(
            report));
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedDistinctForReport(
            report));
        var home = SimpleScanPresentation.Create(
            report, true, 6, 0, 0, []);
        Assert.Equal("Ricerca aggiornamenti non completata", home.Headline);
        Assert.Contains("Windows Update non verificato", home.SourceLine);
        Assert.Contains("Non è stato possibile verificare",
            CompactResultsEmptyMessage.Create(0, report));
        Assert.DoesNotContain("PRIVATE_", home.Headline + home.SourceLine);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 2)]
    [InlineData(-2, -1)]
    public void ImpossibleCompletedCountersNeverCountAsSuccessfulPass(
        int total, int unique)
    {
        var report = Report(Status("Windows Update online", "Completed",
            total, unique));
        Assert.True(report.AllFailed);
        Assert.True(report.IsPartial);
        Assert.False(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(
            report));
        Assert.Contains("non completata",
            SimpleScanPresentation.Create(report, true, 0, 0, 0, [])
                .Headline);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Partial")]
    [InlineData("NetworkError")]
    [InlineData("PolicyRestricted")]
    [InlineData("NotRegistered")]
    public void NoCompletedPassIsNoVerifiedSearch(string outcome)
    {
        var row = outcome == "NotRegistered"
            ? Status("Microsoft Update online", outcome)
            : Status("Windows Update online", outcome);
        var report = Report(row);
        Assert.True(report.AllFailed);
        Assert.True(report.IsPartial == (outcome != "NotRegistered") ||
            outcome == "NotRegistered" && report.IsPartial == false);
        Assert.False(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(
            report));
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Partial")]
    [InlineData("NetworkError")]
    public void SameSourceDuplicatedInAnyOutcomeCannotCountAsSuccess(
        string secondOutcome)
    {
        var completed = Status("Windows Update online", "Completed", 1, 1);
        var duplicate = Status("WINDOWS UPDATE ONLINE", secondOutcome);
        var report = Report(completed, duplicate);

        Assert.True(report.AllFailed);
        Assert.True(report.IsPartial);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedDistinctForReport(
            report));
        Assert.Contains("non completata",
            SimpleScanPresentation.Create(report, true, 4, 0, 0, [])
                .Headline);
    }

    [Fact]
    public void RealCompletedPassWithZeroMatchesIsStillSuccessful()
    {
        var report = Report(Status("Windows configurato", "Completed"));

        Assert.False(report.AllFailed);
        Assert.False(report.IsPartial);
        Assert.True(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(
            report));
        Assert.Contains("Nessun aggiornamento proposto",
            SimpleScanPresentation.Create(report, true, 4, 0, 0, [])
                .Headline);
    }

    [Fact]
    public void OneVerifiedPassKeepsPartialSuccessDespiteOtherInvalidSources()
    {
        var valid = Status("Windows configurato", "Completed", 0);
        var invalid = Status("Windows Update online", "Completed", 0, 1);
        var foreign = Status("PRIVATE_OEM_SOURCE");
        var report = Report(valid, invalid, foreign);

        Assert.False(report.AllFailed);
        Assert.True(report.IsPartial);
        Assert.True(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(
            report));
        Assert.Contains("Analisi parziale",
            SimpleScanPresentation.Create(report, true, 4, 0, 0, [])
                .Headline);
    }

    [Fact]
    public void ValidMicrosoftNotRegisteredCannotBeCountedAsCompleted()
    {
        var report = Report(WindowsDriverSearchPlan.NotRegisteredStatus());

        Assert.True(report.AllFailed);
        Assert.False(report.IsPartial);
        Assert.Empty(WuaSourceFailurePolicy.UnavailableSources(report));
        Assert.Contains("Ricerca aggiornamenti non completata",
            SimpleScanPresentation.Create(report, true, 4, 0, 0, [])
                .Headline);
    }

    [Fact]
    public void OnlySoftwareSuccessDoesNotCountAsDriverSourceSuccess()
    {
        var report = new DriverSearchReport(
            [], [Status("Windows Update online", "NetworkError")],
            RelatedSoftwareSources:
            [Status("Windows Update online · componenti software")]);

        Assert.True(report.AllFailed);
        Assert.True(report.IsPartial);
        Assert.False(report.SoftwareSearchIsPartial);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedForReport(report));
    }

    [Fact]
    public void InvalidPrimaryCompletedAndValidAdvancedCompletedIsPartialSuccess()
    {
        var bad = Status("Windows configurato", "Completed", -1);
        var good = Status("Windows Update online · avanzato",
            "Completed", 2, 1);
        var report = Report(bad, good);

        Assert.False(report.AllFailed);
        Assert.True(report.IsPartial);
        Assert.True(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(
            report));
        Assert.Contains("fonti riuscite",
            SimpleScanPresentation.Create(report, true, 4, 0, 0, [])
                .Headline);
    }
}
