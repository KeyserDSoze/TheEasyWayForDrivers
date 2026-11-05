using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaSourceFailurePolicyTests
{
    [Fact]
    public void MicrosoftUpdateNotRegisteredDoesNotTurnCompletedScanPartial()
    {
        var report = new DriverSearchReport(
            [],
            [
                new DriverSearchPassStatus("Windows configurato", "Completed", 0, "ok"),
                new DriverSearchPassStatus("Windows Update online", "Completed", 0, "ok"),
                WindowsDriverSearchPlan.NotRegisteredStatus()
            ]);

        Assert.False(report.IsPartial);
        Assert.False(report.AllFailed);
        Assert.Empty(WuaSourceFailurePolicy.UnavailableSources(report));

        var text = SimpleScanPresentation.Create(report, true, 335, 3, 0,
            WuaSourceFailurePolicy.UnavailableSources(report));
        Assert.DoesNotContain("Alcuni controlli non sono riusciti", text.Warning);
        Assert.Contains("nessun risultato non significa driver aggiornati", text.Warning);
    }

    [Fact]
    public void FailedMicrosoftUpdateRegistrationStillCountsAsUnverified()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows configurato", "Completed", 0, "ok"),
            WindowsDriverSearchPlan.RegistrationFailureStatus()
        ]);

        var failed = WuaSourceFailurePolicy.UnavailableSources(report);
        Assert.Equal(["Microsoft Update online"], failed);
        Assert.True(report.IsPartial);
        var text = SimpleScanPresentation.Create(report, true, 40, 1, 0, failed);
        Assert.Contains("Alcuni controlli non sono riusciti", text.Warning);
    }

    [Fact]
    public void FailedPassRemainsReportedButRegisteredAbsenceIsNotError()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows configurato", "Completed", 2, "ok"),
            new DriverSearchPassStatus("Windows Update online", "Failed", 0, "offline"),
            WindowsDriverSearchPlan.NotRegisteredStatus()
        ]);
        Assert.Equal(["Windows Update online"],
            WuaSourceFailurePolicy.UnavailableSources(report));
        Assert.True(report.IsPartial);
    }

    [Fact]
    public void NullReportHasNoPerSourceFailures() =>
        Assert.Empty(WuaSourceFailurePolicy.UnavailableSources(null));

    [Fact]
    public void SoftwareOnlyFailureIsReportedSeparatelyFromDriverSourceFailures()
    {
        var report = new DriverSearchReport(
            [],
            [new DriverSearchPassStatus("Windows configurato",
                "Completed", 0, "OK")],
            RelatedSoftwareSources:
            [new DriverSearchPassStatus("Windows Update online · componenti software",
                "Failed", 0, "Offline")]);

        Assert.Empty(WuaSourceFailurePolicy.UnavailableSources(report));
        Assert.False(report.IsPartial);
        Assert.True(report.SoftwareSearchIsPartial);
    }
}
