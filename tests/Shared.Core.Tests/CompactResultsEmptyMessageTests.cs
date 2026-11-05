using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class CompactResultsEmptyMessageTests
{
    [Fact]
    public void MissingReportDoesNotClaimNoUpdates()
    {
        var message = CompactResultsEmptyMessage.Create(0, null);
        Assert.Contains("Non è stato possibile verificare", message);
        Assert.DoesNotContain("Nessun aggiornamento proposto", message);
    }

    [Fact]
    public void FailedScanDoesNotClaimNoUpdates()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows Update", "Failed", 0, "Offline")
        ]);
        Assert.Contains("Non è stato possibile verificare",
            CompactResultsEmptyMessage.Create(0, report));
    }

    [Fact]
    public void PartiallySuccessfulScanIsExplicitlyIncomplete()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Configured", "Completed", 0, "OK"),
            new DriverSearchPassStatus("Online", "Failed", 0, "Offline")
        ]);
        var message = CompactResultsEmptyMessage.Create(0, report);
        Assert.Contains("incompleta", message);
        Assert.Contains("fonti che hanno risposto", message);
    }

    [Fact]
    public void SuccessfulEmptySearchIsLimitedToQueriedSources()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Configured", "Completed", 0, "OK")
        ]);
        var message = CompactResultsEmptyMessage.Create(0, report);
        Assert.Contains("servizi Windows consultati", message);
        Assert.Contains("non significa", message);
    }

    [Fact]
    public void FilterEmptyExplainsHowToSeeAllCandidatesEvenIfCoveragePartial()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Online", "Failed", 0, "Offline")
        ]);
        var message = CompactResultsEmptyMessage.Create(3, report);
        Assert.Contains("Tutti i risultati", message);
        Assert.DoesNotContain("Nessun aggiornamento proposto", message);
    }

    [Fact]
    public void SearchWithoutMatchesExplainsHowToAdjustText()
    {
        var message = CompactResultsEmptyMessage.Create(5, null, "nvidia");
        Assert.Contains("Nessun driver corrisponde", message);
        Assert.Contains("Modifica il testo", message);
    }

    [Fact]
    public void SearchTextDoesNotHideIncompleteScanWhenNoUpdatesWereReturned()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Online", "Failed", 0, "Offline")
        ]);
        Assert.Contains("Non è stato possibile verificare",
            CompactResultsEmptyMessage.Create(0, report, "NVIDIA"));
    }
}
