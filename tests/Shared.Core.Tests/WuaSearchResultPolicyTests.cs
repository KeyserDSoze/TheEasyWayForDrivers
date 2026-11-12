using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaSearchResultPolicyTests
{
    [Fact]
    public void OnlySuccessfulSearchWithoutWarningsIsComplete() =>
        WuaSearchResultPolicy.RequireComplete(2, 0);

    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 15)]
    [InlineData(3, 0)]
    [InlineData(3, 2)]
    public void CompletedWithWarningsOrErrorsIsPartial(int code, int warningCount) =>
        Assert.Throws<WuaPartialSearchException>(() =>
            WuaSearchResultPolicy.RequireComplete(code, warningCount));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(-1)]
    [InlineData(50)]
    public void NonSuccessfulResultCodesCannotBeReportedAsZeroDrivers(int code) =>
        Assert.Throws<InvalidDataException>(() =>
            WuaSearchResultPolicy.RequireComplete(code, 0));

    [Fact]
    public void InvalidWarningCountCannotBeTreatedAsComplete() =>
        Assert.Throws<InvalidDataException>(() =>
            WuaSearchResultPolicy.RequireComplete(2, -1));

    [Fact]
    public void AuditedPartialPassDiscardsUnverifiedCandidatesAndPreservesOtherSources()
    {
        var logged = false;
        var partial = DriverSearchPassAudit.Run(
            "Windows Update online",
            () => throw new WuaPartialSearchException(),
            CancellationToken.None,
            _ => logged = true);
        Assert.True(logged);
        Assert.Equal("Partial", partial.Outcome);
        Assert.False(partial.Succeeded);
        Assert.Equal(0, partial.Candidates);
        Assert.Equal(0, partial.NewUniqueCandidates);
        Assert.Contains("non convalidati", partial.Detail);

        var complete = DriverSearchPassAudit.Run(
            "Windows configurato", () => 2, CancellationToken.None);
        var report = new DriverSearchReport(
            [new DriverUpdateInfo(Guid.NewGuid().ToString("D"),
                "Intel test", null, "Net", "Intel", null, null,
                false, "Intel", null, @"PCI\VEN_8086&DEV_51F1", null,
                SearchSource: "Windows configurato")],
            [complete, partial]);

        Assert.True(report.IsPartial);
        Assert.False(report.AllFailed);
        Assert.Single(report.Updates);
        Assert.Equal(["Windows Update online"],
            WuaSourceFailurePolicy.UnavailableSources(report));

        var text = ScanCoverageText.Create(report, [], 1, 0, 0);
        Assert.Contains("ricerca con avvisi/errori", text);
        Assert.Contains("1 candidati distinti", text);
        Assert.DoesNotContain(@"PCI\VEN", text);
    }

    [Fact]
    public void SoftwareOnlyPartialDoesNotBlockCompletedDriverPass()
    {
        var report = new DriverSearchReport(
            [], [DriverSearchPassAudit.Run("Windows Update online", () => 0,
                CancellationToken.None)],
            RelatedSoftwareSources: [
                DriverSearchPassAudit.Run("WU software",
                    () => throw new WuaPartialSearchException(),
                    CancellationToken.None)
            ]);

        Assert.False(report.IsPartial);
        Assert.False(report.AllFailed);
        Assert.True(report.SoftwareSearchIsPartial);
        Assert.Empty(WuaSourceFailurePolicy.UnavailableSources(report));
    }

    [Fact]
    public void AllPartialSourcesAreUnverifiedNotSuccessfulZeroResult()
    {
        var partial = DriverSearchPassAudit.Run(
            "WU online", () => throw new WuaPartialSearchException(),
            CancellationToken.None);
        var report = new DriverSearchReport([], [partial]);

        Assert.True(report.AllFailed);
        Assert.True(report.IsPartial);
        Assert.Contains("non convalidati", partial.Detail);
    }
}
