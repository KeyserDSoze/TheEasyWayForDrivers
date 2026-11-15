using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #470: a failed automatic Dell/HP catalog request is not a successful
/// OEM metadata query, but is independent from completed WUA passes.
/// Retrying it must never clear other partial-scan evidence or create
/// additional driver update IDs / installable packages.
/// </summary>
public sealed class AutomaticOemCatalogCoverageTests
{
    private const string GuidId = "00000000-0000-4000-8000-000000000470";

    private static DriverSearchReport WuaReport() =>
        new([
            new DriverUpdateInfo(GuidId, "Intel network driver",
                null, "Net", "Microsoft", null, null, false,
                "Microsoft", null, @"PCI\VEN_8086&DEV_51F1", null,
                SearchSource: "Windows Update online")
        ], [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "PRIVATE_WSUS_SERVER", 1)
        ]);

    private static SimpleScanPresentation Home(
        DriverSearchReport report, IReadOnlyCollection<string> unavailable,
        int oemApplicable = 0) =>
        SimpleScanPresentation.Create(
            report, inventoryComplete: true,
            inventoriedDevices: 8, problems: 0,
            oemRecommended: oemApplicable, unavailableSteps: unavailable);

    [Fact]
    public void DellOrHpTimeoutMakesHomePartialWithoutLosingVerifiedWua()
    {
        var report = WuaReport();
        var failedCatalog = AutomaticOemCatalogCoverage.WithOutcome(
            [], isVerificationIncomplete: true);
        var summary = Home(report, failedCatalog);

        Assert.Equal([AutomaticOemCatalogCoverage.UnavailableStep],
            failedCatalog);
        Assert.True(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report));
        Assert.False(report.IsPartial);
        Assert.StartsWith("Risultati parziali:", summary.Headline);
        Assert.Contains("1 possibile aggiornamento", summary.Headline);
        Assert.Contains("1 candidati Windows Update", summary.SourceLine);
        Assert.Contains("1 ordinari selezionabili", summary.SourceLine);
        Assert.Contains("0 segnalati dal produttore", summary.SourceLine);
        Assert.Contains("risultati possono essere incompleti",
            summary.Warning);
        Assert.Contains("Catalogo automatico Dell/HP non verificato",
            summary.Warning);
        Assert.Contains("Riprova catalogo OEM", summary.Warning);
        Assert.Contains("I risultati WUA non cambiano", summary.Warning);
        Assert.Single(WindowsDriverCandidateMerge
            .ConfirmedDistinctForReport(report));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(report.Updates));
        Assert.DoesNotContain("PRIVATE_", summary.Headline +
            summary.SourceLine + summary.Warning);
        Assert.DoesNotContain(GuidId, summary.SourceLine);
    }

    [Fact]
    public void ExplicitRetrySuccessRemovesOnlyCatalogGapNotOtherSourceFailure()
    {
        var initial = AutomaticOemCatalogCoverage.WithOutcome([
            "ricerca Windows Update", "catalogo OEM ufficiale",
            "inventario dispositivi", "catalogo OEM ufficiale"
        ], isVerificationIncomplete: true);
        Assert.Equal(3, initial.Count);

        var afterRetry = AutomaticOemCatalogCoverage.WithOutcome(
            initial, isVerificationIncomplete: false);
        Assert.Equal(["ricerca Windows Update", "inventario dispositivi"],
            afterRetry);
        Assert.DoesNotContain(AutomaticOemCatalogCoverage.UnavailableStep,
            afterRetry);

        var againFailed = AutomaticOemCatalogCoverage.WithOutcome(
            afterRetry, isVerificationIncomplete: true);
        Assert.Equal(3, againFailed.Count);
        Assert.Equal(1, againFailed.Count(s =>
            s == AutomaticOemCatalogCoverage.UnavailableStep));
        Assert.Equal("ricerca Windows Update", againFailed.First());
    }

    [Fact]
    public void SuccessfulZeroRowCatalogCannotBeReportedAsQueryFailure()
    {
        var result = AutomaticOemCatalogCoverage.WithOutcome(
            [], isVerificationIncomplete: false);
        Assert.Empty(result);

        var report = WuaReport();
        var summary = Home(report, result);
        Assert.Equal("1 possibile aggiornamento rilevato", summary.Headline);
        Assert.DoesNotContain("Risultati parziali", summary.Headline);
        Assert.DoesNotContain("Alcuni controlli driver non sono riusciti",
            summary.Warning);
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(report.Updates));
    }

    [Fact]
    public void CatalogFailureDoesNotTurnNoUpdatesIntoVerifiedAbsence()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows configurato",
                "Completed", 0, "ok", 0)
        ]);
        var failed = AutomaticOemCatalogCoverage.WithOutcome(
            [], isVerificationIncomplete: true);
        var summary = Home(report, failed);
        Assert.StartsWith("Analisi parziale:", summary.Headline);
        Assert.Contains("nessun aggiornamento verificato",
            summary.Headline);
        Assert.Contains("0 candidati Windows Update", summary.SourceLine);
        Assert.Contains("0 segnalati dal produttore", summary.SourceLine);
        Assert.DoesNotContain("Nessun aggiornamento proposto", summary.Headline);
        Assert.Contains("risultati possono essere incompleti", summary.Warning);
    }

    [Fact]
    public void RealManufacturerRecommendationIsKeptSeparateDuringCatalogFailure()
    {
        var failed = AutomaticOemCatalogCoverage.WithOutcome(
            [], isVerificationIncomplete: true);
        var summary = Home(WuaReport(), failed, oemApplicable: 2);
        Assert.Contains("3 possibili aggiornamenti", summary.Headline);
        Assert.Contains("1 candidati Windows Update", summary.SourceLine);
        Assert.Contains("2 segnalati dal produttore", summary.SourceLine);
        Assert.Contains("non sono aggiornamenti", summary.SourceLine);
    }

    [Fact]
    public void StatusIsAnonymousAndCannotCarryModelSerialOrCatalogEndpoints()
    {
        var failed = AutomaticOemCatalogCoverage.WithOutcome(
            ["ricerca WUA parziale"], isVerificationIncomplete: true);
        var status = string.Join(" · ", failed);
        Assert.DoesNotContain("DELL", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HP", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\\", status);
        Assert.DoesNotContain(GuidId, status);
        Assert.DoesNotContain("PRIVATE_", status);
        Assert.Throws<ArgumentNullException>(() =>
            AutomaticOemCatalogCoverage.WithOutcome(null!, true));
    }
}
