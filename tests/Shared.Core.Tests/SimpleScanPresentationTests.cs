using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class SimpleScanPresentationTests
{
    private static DriverUpdateInfo Update(string id) =>
        new(id, "NVIDIA driver", null, "Display", "NVIDIA", null,
            null, false, "NVIDIA", null, null, null);

    [Fact]
    public void EmptySuccessfulWuaDoesNotClaimThePcIsUpdated()
    {
        var report = new DriverSearchReport([],
        [
            new DriverSearchPassStatus("Windows configurato", "Completed", 0, "OK")
        ]);

        var screen = SimpleScanPresentation.Create(
            report, true, 125, 0, 0, []);

        Assert.Contains("Nessun aggiornamento proposto", screen.Headline);
        Assert.Contains("0 candidati Windows Update", screen.SourceLine);
        Assert.Contains("125 dispositivi", screen.DeviceLine);
        Assert.Contains("non significa driver aggiornati", screen.Warning);
    }

    [Fact]
    public void EightRecognizedDevicesWithTwoWuaMatchesAreOnlyTwoCandidates()
    {
        var report = new DriverSearchReport(
            [Update("id-a"), Update("id-b")],
            [new DriverSearchPassStatus("Windows Update online", "Completed", 2, "OK")]);

        var screen = SimpleScanPresentation.Create(
            report, true, 8, 0, 0, []);

        Assert.Equal("2 possibili aggiornamenti rilevati", screen.Headline);
        Assert.Contains("2 candidati Windows Update", screen.SourceLine);
        Assert.Contains("8 dispositivi esaminati", screen.DeviceLine);
        Assert.DoesNotContain("8 possibili aggiornamenti", screen.Headline);
    }

    [Fact]
    public void OnlyManufacturerAssessedPackagesAddToUpdateHeadline()
    {
        var report = new DriverSearchReport(
            [Update("id")],
            [new DriverSearchPassStatus("Windows configurato", "Completed", 1, "OK")]);

        var screen = SimpleScanPresentation.Create(
            report, true, 50, 1, 2, []);
        Assert.Contains("3 possibili aggiornamenti", screen.Headline);
        Assert.Contains("2 segnalati dal produttore", screen.SourceLine);
        Assert.Contains("1 con problemi Windows", screen.DeviceLine);
    }

    [Fact]
    public void FailedQueryNeverReportedAsZeroUpdates()
    {
        var failed = new DriverSearchReport([],
            [new DriverSearchPassStatus("Windows Update online", "Failed", 0, "Error")]);
        var screen = SimpleScanPresentation.Create(failed, true, 6, 1, 0, []);
        Assert.Equal("Ricerca aggiornamenti non completata", screen.Headline);
        Assert.Contains("Windows Update non verificato", screen.SourceLine);
        Assert.Contains("disponibilità", screen.Warning);
    }

    [Fact]
    public void PartialSearchCannotClaimTotalCoverage()
    {
        var partial = new DriverSearchReport([Update("id")],
        [
            new DriverSearchPassStatus("Windows configurato", "Completed", 1, "OK"),
            new DriverSearchPassStatus("Microsoft Update online", "Failed", 0, "timeout")
        ]);
        var screen = SimpleScanPresentation.Create(
            partial, true, 10, 0, 0, ["Microsoft Update"]);

        Assert.Contains("1 possibile aggiornamento", screen.Headline);
        Assert.Contains("risultati possono essere incompleti", screen.Warning);
    }

    [Fact]
    public void NoInventoryIsNotFabricatedIntoDeviceCount()
    {
        var report = new DriverSearchReport(
            [], [new DriverSearchPassStatus("WU", "Completed", 0, "OK")]);
        var screen = SimpleScanPresentation.Create(report, false, 100, 0, 0, []);

        Assert.Equal("Inventario dispositivi non disponibile.", screen.DeviceLine);
        Assert.DoesNotContain("100", screen.DeviceLine);
    }

    [Fact]
    public void SoftwareFailureIsClearlyInformationalNotADriverQueryFailure()
    {
        var report = new DriverSearchReport(
            [Update("driver-id")],
            [new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "OK")],
            RelatedSoftwareSources:
            [new DriverSearchPassStatus("Windows Update online · componenti software",
                "Failed", 0, "Offline")]);

        var screen = SimpleScanPresentation.Create(
            report, true, 8, 0, 0, []);

        Assert.Equal("1 possibile aggiornamento rilevato", screen.Headline);
        Assert.Contains("Ricerca driver Windows completata", screen.Warning);
        Assert.Contains("software OEM", screen.Warning);
        Assert.DoesNotContain("Alcuni controlli driver non sono riusciti", screen.Warning);
    }
}
