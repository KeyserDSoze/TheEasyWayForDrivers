using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverSearchReportTests
{
    [Fact]
    public void PartialCoverageDoesNotDiscardGoodUpdateResults()
    {
        var result = new DriverSearchReport(
            [new DriverUpdateInfo("id", "Intel", null, null, null, null,
                null, false, null, null, null, null)],
            [
                new DriverSearchPassStatus("Configured", "Completed", 1, "OK"),
                new DriverSearchPassStatus("WU online", "Failed", 0, "Not verified")
            ]);
        Assert.True(result.IsPartial);
        Assert.False(result.AllFailed);
        Assert.Single(result.Updates);
    }

    [Fact]
    public void AllFailedIsNotEmptySuccessfulScan()
    {
        var report = new DriverSearchReport(
            [],
            [new DriverSearchPassStatus("WU online", "Failed", 0, "Failed")]);
        Assert.True(report.AllFailed);
        Assert.True(report.IsPartial);
    }

    [Fact]
    public void CompleteEmptyIsActuallyCompleteWithinQueriedSources()
    {
        var report = new DriverSearchReport(
            [],
            [new DriverSearchPassStatus("Windows configurato", "Completed", 0, "Empty")]);
        Assert.False(report.IsPartial);
        Assert.False(report.AllFailed);
    }

    [Fact]
    public void InformationalSoftwareFailureDoesNotPoisonCompletedDriverScan()
    {
        var report = new DriverSearchReport(
            [],
            [new DriverSearchPassStatus("Windows Update online",
                "Completed", 0, "Driver search OK")],
            RelatedSoftwareSources:
            [new DriverSearchPassStatus("Windows Update online · componenti software",
                "Failed", 0, "Software query offline")]);

        Assert.False(report.AllFailed);
        Assert.False(report.IsPartial);
        Assert.True(report.SoftwareSearchIsPartial);
    }

    [Fact]
    public void DriverFailureStillBlocksFullCoverageEvenWhenSoftwareWorks()
    {
        var report = new DriverSearchReport(
            [],
            [
                new DriverSearchPassStatus("Windows configurato",
                    "Completed", 1, "OK"),
                new DriverSearchPassStatus("Windows Update online",
                    "Failed", 0, "Offline")
            ],
            RelatedSoftwareSources:
            [new DriverSearchPassStatus("Windows Update online · componenti software",
                "Completed", 2, "OK")]);

        Assert.True(report.IsPartial);
        Assert.False(report.SoftwareSearchIsPartial);
    }


    [Fact]
    public void OldIpcPassWithoutUtcFieldStillDeserializes()
    {
        const string json = """
            {"Source":"Windows Update online","Outcome":"Completed","Candidates":0,"Detail":"OK","NewUniqueCandidates":0}
            """;
        var old = System.Text.Json.JsonSerializer.Deserialize<DriverSearchPassStatus>(json);
        Assert.NotNull(old);
        Assert.True(old.Succeeded);
        Assert.Null(old.AttemptedAtUtc);
        Assert.Equal(0, old.Candidates);
    }

    [Fact]
    public void PolicyAndNetworkPassFailuresKeepSourceCoveragePartial()
    {
        foreach (var outcome in new[] { "PolicyRestricted", "NetworkError" })
        {
            var report = new DriverSearchReport([], [
                new DriverSearchPassStatus("Windows configurato", "Completed", 0, ""),
                new DriverSearchPassStatus("Windows Update online", outcome, 0, "")
            ]);
            Assert.True(report.IsPartial);
            Assert.False(report.AllFailed);
        }
    }

    [Fact]
    public void SoftwareSourceNotRegisteredIsNotAnAttemptedFailure()
    {
        var report = new DriverSearchReport(
            [],
            [new DriverSearchPassStatus("Windows configurato",
                "Completed", 0, "OK")],
            RelatedSoftwareSources:
            [new DriverSearchPassStatus("Microsoft Update software",
                "NotRegistered", 0, "not searched")]);

        Assert.False(report.IsPartial);
        Assert.False(report.SoftwareSearchIsPartial);
    }
}
