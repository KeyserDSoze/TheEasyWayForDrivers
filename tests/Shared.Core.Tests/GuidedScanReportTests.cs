using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class GuidedScanReportTests
{
    [Fact]
    public void AllStagesComplete_ReportsCandidatesWithoutCallingThemVerifiedDrivers()
    {
        var report = GuidedScanReport.Create(
            true, true, true, 333, 3, 2, 4, 0, 3,
            "Contoso Laptop 16", []);

        Assert.Equal("Analisi terminata", report.Headline);
        Assert.Contains("333 dispositivi", report.DeviceSummary);
        Assert.Contains("PC rilevato: Contoso Laptop 16", report.DeviceSummary);
        Assert.Contains("2 candidati normali", report.SourceSummary);
        Assert.Contains("4 avanzati/nascosti", report.SourceSummary);
        Assert.Contains("0 dispositivi correlati", report.SourceSummary);
        Assert.Contains("3 (canali di supporto, non pacchetti verificati)", report.SourceSummary);
        Assert.Contains("non sono tutti interrogabili automaticamente", report.Caveat);
    }

    [Fact]
    public void WindowsUpdateFailure_DoesNotShowFalseZeroOrCompletedScan()
    {
        var report = GuidedScanReport.Create(
            true, false, true, 150, 1, 0, 0, 0, 2,
            null, ["ricerca Windows Update"]);

        Assert.Contains("parziale", report.Headline);
        Assert.Contains("Windows Update: ricerca non riuscita", report.SourceSummary);
        Assert.DoesNotContain("0 candidati normali", report.SourceSummary);
        Assert.Contains("ricerca Windows Update", report.Caveat);
    }

    [Fact]
    public void InventoryAndOemUnavailable_DoNotClaimDevicesOrVendorUpdates()
    {
        var report = GuidedScanReport.Create(
            false, true, false, 0, 0, 1, 0, 0, 0,
            "stale model", ["inventario dispositivi", "riconoscimento fonti OEM"]);

        Assert.Contains("parziale", report.Headline);
        Assert.DoesNotContain("stale model", report.DeviceSummary);
        Assert.Contains("Non è stato possibile leggere", report.DeviceSummary);
        Assert.Contains("Fonti OEM: verifica non riuscita", report.SourceSummary);
        Assert.DoesNotContain("Fonti ufficiali OEM applicabili: 0", report.SourceSummary);
    }

    [Fact]
    public void FullButEmptySearch_DoesNotPromiseExhaustiveCoverage()
    {
        var report = GuidedScanReport.Create(
            true, true, true, 0, 0, 0, 0, 0, 0, null, []);

        Assert.Equal("Analisi terminata", report.Headline);
        Assert.Contains("0 candidati normali", report.SourceSummary);
        Assert.Contains("Un candidato WUA non è prova", report.Caveat);
    }
}
