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
            [new DriverSearchPassStatus("Configured", "Completed", 0, "Empty")]);
        Assert.False(report.IsPartial);
        Assert.False(report.AllFailed);
    }
}
