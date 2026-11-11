using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class SimpleScanPresentationTests
{
    private static DriverUpdateInfo Update(
        string id, string source = "Windows Update online") =>
        new(id == "id-b"
                ? "00000000-0000-4000-8000-000000000376"
                : "00000000-0000-4000-8000-000000000375",
            "NVIDIA driver", null, "Display", "NVIDIA", null,
            null, false, "NVIDIA", null, null, null,
            SearchSource: source);

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
            [Update("id", "Windows configurato")],
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
        var partial = new DriverSearchReport(
            [Update("id", "Windows configurato")],
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

    [Fact]
    public void ZeroCandidatesWithFailedPassHasExplicitPartialHeadline()
    {
        var report = new DriverSearchReport([],
        [
            new DriverSearchPassStatus("Windows configurato",
                "Completed", 0, "OK"),
            new DriverSearchPassStatus("Windows Update online",
                "NetworkError", 0, "unavailable")
        ]);
        var result = SimpleScanPresentation.Create(
            report, true, 250, 0, 0, []);

        Assert.True(report.IsPartial);
        Assert.False(report.AllFailed);
        Assert.Contains("Analisi parziale", result.Headline);
        Assert.Contains("nessun aggiornamento verificato", result.Headline);
        Assert.DoesNotContain("Nessun aggiornamento proposto",
            result.Headline);
        Assert.Contains("solo fonti riuscite", result.SourceLine);
        Assert.Contains("copertura PARZIALE", result.SourceLine);
        Assert.Contains("risultati possono essere incompleti",
            result.Warning);
    }

    [Fact]
    public void VerifiedCandidateFromPartialSearchKeepsCountAndExplicitCaveat()
    {
        var report = new DriverSearchReport(
            [Update("verified-guid", "Windows configurato")],
            [
                new DriverSearchPassStatus("Windows configurato",
                    "Completed", 1, "ok"),
                new DriverSearchPassStatus("Windows Update online",
                    "Partial", 11, "not verified")
            ]);
        var screen = SimpleScanPresentation.Create(
            report, true, 8, 0, 0, []);

        Assert.StartsWith("Risultati parziali:", screen.Headline);
        Assert.Contains("1 possibile aggiornamento", screen.Headline);
        Assert.Contains("1 candidati Windows Update", screen.SourceLine);
        Assert.Contains("solo fonti riuscite", screen.SourceLine);
        Assert.DoesNotContain("11 candidati", screen.SourceLine);
    }

    [Fact]
    public void UnregisteredMicrosoftUpdateDoesNotMakeHomePartial()
    {
        var report = new DriverSearchReport([],
        [
            new DriverSearchPassStatus("Windows configurato",
                "Completed", 0, "ok"),
            TheEasyWayForDrivers.Core.Update.WindowsDriverSearchPlan.NotRegisteredStatus()
        ]);
        var screen = SimpleScanPresentation.Create(
            report, true, 20, 0, 0, []);

        Assert.False(report.IsPartial);
        Assert.Contains("Nessun aggiornamento proposto",
            screen.Headline);
        Assert.DoesNotContain("parziale", screen.Headline.ToLowerInvariant());
        Assert.DoesNotContain("PARZIALE", screen.SourceLine);
    }

    [Fact]
    public void InventoryMissingMakesGuidedHeadlineExplicitlyPartial()
    {
        var report = new DriverSearchReport([],
        [new DriverSearchPassStatus("Windows configurato",
            "Completed", 0, "ok")]);
        var screen = SimpleScanPresentation.Create(
            report, false, 120, 0, 0, []);

        Assert.Contains("Analisi parziale", screen.Headline);
        Assert.Contains("Inventario dispositivi non disponibile",
            screen.DeviceLine);
        Assert.Contains("risultati possono essere incompleti",
            screen.Warning);
    }

    [Fact]
    public void HomeDoesNotCountFailedPassRowsDespiteAnotherSourceSucceeding()
    {
        var report = new DriverSearchReport(
            [Update("id-a", "Windows Update online")],
            [new DriverSearchPassStatus("Windows configurato", "Completed", 0, "OK"),
             new DriverSearchPassStatus("Windows Update online", "NetworkError", 0, "PRIVATE")]);
        var summary = SimpleScanPresentation.Create(report, true, 8, 0, 0, []);
        Assert.Contains("Analisi parziale: nessun aggiornamento verificato",
            summary.Headline);
        Assert.Contains("0 candidati Windows Update", summary.SourceLine);
        Assert.Contains("copertura PARZIALE", summary.SourceLine);
        Assert.DoesNotContain("1 possibile aggiornamento", summary.Headline);
    }

    [Fact]
    public void HomeAndMainListDeduplicateOneGuidAcrossWuaSources()
    {
        var original = Update("id-a", "Windows configurato");
        var duplicate = original with
        {
            Id = "{" + original.Id + "}",
            SearchSource = "Microsoft Update online"
        };
        var report = new DriverSearchReport([original, duplicate],
        [
            new DriverSearchPassStatus("Windows configurato", "Completed", 1, "OK"),
            new DriverSearchPassStatus("Microsoft Update online", "Completed", 1, "OK")
        ]);
        var summary = SimpleScanPresentation.Create(report, true, 5, 0, 0, []);
        Assert.Equal("1 possibile aggiornamento rilevato", summary.Headline);
        Assert.Contains("1 candidati Windows Update", summary.SourceLine);
        Assert.Single(WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
    }

    [Fact]
    public void UnknownOrMalformedWuaRecordCannotCreateFalseHomeHeadline()
    {
        var report = new DriverSearchReport(
        [
            Update("id-a", "Windows Update online"),
            new DriverUpdateInfo("PRIVATE_FAKE_ID", "driver", null,
                "Display", "Microsoft", null, null, false, "Microsoft",
                null, null, null, SearchSource: "Windows configurato")
        ],
        [
            new DriverSearchPassStatus("Unknown OEM source", "Completed", 1, "OK"),
            new DriverSearchPassStatus("Windows Update online", "Failed", 0, "Offline"),
            new DriverSearchPassStatus("Windows configurato", "Completed", 1, "OK")
        ]);
        var summary = SimpleScanPresentation.Create(report, true, 5, 0, 0, []);
        Assert.Contains("0 candidati Windows Update", summary.SourceLine);
        Assert.Contains("Analisi parziale", summary.Headline);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
    }

    [Fact]
    public void OnlyOemRecommendedPackagesAreCountedWhenWuaUnavailable()
    {
        var report = new DriverSearchReport([],
        [
            new DriverSearchPassStatus("Unknown OEM source", "Completed", 0, ""),
            new DriverSearchPassStatus("Windows Update online", "Failed", 0, "Offline")
        ]);
        var summary = SimpleScanPresentation.Create(report, true, 8, 0, 2, []);
        Assert.Contains("Risultati parziali: 2 possibili aggiornamenti", summary.Headline);
        Assert.Contains("Windows Update non verificato", summary.SourceLine);
        Assert.Contains("2 segnalati dal produttore", summary.SourceLine);
    }

    [Fact]
    public void SoftwareOnlyFailureDoesNotTurnEmptyDriverResultPartial()
    {
        var report = new DriverSearchReport([],
            [new DriverSearchPassStatus("Windows configurato",
                "Completed", 0, "ok")],
            RelatedSoftwareSources:
            [new DriverSearchPassStatus(
                "Windows configurato · componenti software",
                "NetworkError", 0, "error")]);
        var screen = SimpleScanPresentation.Create(
            report, true, 60, 0, 0, []);

        Assert.False(report.IsPartial);
        Assert.True(report.SoftwareSearchIsPartial);
        Assert.Contains("Nessun aggiornamento proposto",
            screen.Headline);
        Assert.DoesNotContain("Risultati parziali", screen.Headline);
        Assert.Contains("software OEM", screen.Warning);
    }

}
