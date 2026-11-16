using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #512: Home's possible-update headline must count only ordinary WUA
/// GUIDs. Manufacturer advice and optional/advanced/firmware WUA records
/// remain explicitly separate non-installable review evidence.
/// </summary>
public sealed class SimpleScanWuaOemHeadlineSeparationTests
{
    private const string Normal = "Windows Update online";
    private const string Configured = "Windows configurato";
    private const string Advanced = "Windows Update online · avanzato";

    private static DriverSearchPassStatus Pass(
        string source, string outcome = "Completed", int count = 0) =>
        new(source, outcome, count, "PRIVATE_PROXY_ENDPOINT");

    private static DriverUpdateInfo Update(int index,
        string source = Normal, string title = "NVIDIA Display Driver",
        bool optional = false, bool advanced = false,
        string driverClass = "Display") =>
        new($"00000000-0000-4000-8000-{index:D12}",
            title, null, driverClass, "Microsoft",
            null, null, false, "Microsoft", null,
            @"PCI\VEN_10DE&DEV_28A0", null,
            IsOptional: optional, IsAdvancedCandidate: advanced,
            SearchSource: source);

    private static SimpleScanPresentation Home(
        DriverSearchReport? report, int oem = 0,
        IReadOnlyCollection<string>? unavailable = null) =>
        SimpleScanPresentation.Create(
            report, inventoryComplete: true, inventoriedDevices: 8,
            problems: 0, oemRecommended: oem,
            unavailableSteps: unavailable ?? []);

    [Fact]
    public void OemOnlyWhenWuaCompletedZeroDoesNotClaimThreeUpdates()
    {
        var report = new DriverSearchReport([], [Pass(Configured)]);
        var home = Home(report, oem: 3);

        Assert.Equal(
            "Nessun candidato Windows Update dalle fonti consultate · " +
            "3 segnalazioni OEM da verificare", home.Headline);
        Assert.Contains("0 candidati Windows Update", home.SourceLine);
        Assert.Contains("3 segnalati dal produttore", home.SourceLine);
        Assert.Contains("NON entrano in Aggiorna tutto", home.SourceLine);
        Assert.DoesNotContain("3 possibili aggiornamenti", home.Headline);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
    }

    [Fact]
    public void FailedWindowsSearchCannotBeRebrandedAsTwoOemUpdates()
    {
        var report = new DriverSearchReport(
            [Update(1)], [Pass(Normal, "NetworkError")]);
        var home = Home(report, oem: 2);

        Assert.Equal("Ricerca aggiornamenti non completata · " +
                     "2 segnalazioni OEM da verificare", home.Headline);
        Assert.Contains("Windows Update non verificato", home.SourceLine);
        Assert.Contains("2 segnalati dal produttore", home.SourceLine);
        Assert.Contains("disponibilità", home.Warning);
        Assert.DoesNotContain("possibili aggiornamenti", home.Headline);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedForReport(report));
    }

    [Fact]
    public void OneOrdinaryThreeReviewAndTwoOemNeverBecomeSixUpdates()
    {
        var report = new DriverSearchReport(
            [
                Update(1),
                Update(2, title: "NVIDIA Wireless", optional: true),
                Update(3, title: "NVIDIA GPU Firmware", driverClass: "Firmware"),
                Update(4, source: Advanced,
                    title: "NVIDIA platform extension", advanced: true)
            ],
            [Pass(Normal, count: 3), Pass(Advanced, count: 1)]);
        var home = Home(report, oem: 2);
        var confirmed = WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report);

        Assert.Equal("1 possibile aggiornamento rilevato · " +
                     "3 risultati Windows Update da verificare · " +
                     "2 segnalazioni OEM da verificare", home.Headline);
        Assert.Equal(4, confirmed.Count);
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(confirmed));
        Assert.Contains("4 candidati Windows Update", home.SourceLine);
        Assert.Contains("1 ordinari selezionabili", home.SourceLine);
        Assert.Contains("3 da verificare", home.SourceLine);
        Assert.Contains("NON entrano in Aggiorna tutto", home.SourceLine);
        Assert.DoesNotContain("6 possibili aggiornamenti", home.Headline);
    }

    [Fact]
    public void PartialWuaAndIndependentOemFailureKeepTheirWarnings()
    {
        var report = new DriverSearchReport([Update(1, source: Configured)],
            [Pass(Configured, count: 1),
             Pass(Normal, "PolicyRestricted")]);
        var home = Home(report, oem: 2,
            unavailable: [AutomaticOemCatalogCoverage.UnavailableStep]);

        Assert.Equal("Risultati parziali: 1 possibile aggiornamento rilevato · " +
                     "2 segnalazioni OEM da verificare", home.Headline);
        Assert.Contains("solo fonti riuscite, copertura PARZIALE",
            home.SourceLine);
        Assert.Contains("Catalogo automatico Dell/HP non verificato",
            home.Warning);
        Assert.Contains("2 segnalati dal produttore", home.SourceLine);
        Assert.Single(WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
    }

    [Fact]
    public void ReviewOnlyAndOemCannotCreateOrdinaryWuaUpdateCount()
    {
        var report = new DriverSearchReport(
            [Update(1, optional: true),
             Update(2, source: Advanced, advanced: true)],
            [Pass(Normal, count: 1), Pass(Advanced, count: 1)]);
        var home = Home(report, oem: 1);

        Assert.Equal("2 risultati Windows Update da verificare · " +
                     "1 segnalazione OEM da verificare", home.Headline);
        Assert.Contains("0 ordinari selezionabili", home.SourceLine);
        Assert.Contains("2 da verificare", home.SourceLine);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report)));
        Assert.DoesNotContain("possibile aggiornamento rilevato", home.Headline);
    }

    [Fact]
    public void PlainOrdinaryWuaWithoutOemKeepsFamiliarHeadline()
    {
        var report = new DriverSearchReport(
            [Update(1), Update(2)],
            [Pass(Normal, count: 2)]);
        var home = Home(report);

        Assert.Equal("2 possibili aggiornamenti rilevati", home.Headline);
        Assert.DoesNotContain("OEM da verificare", home.Headline);
        Assert.Equal(2, QuickUpdatePolicy.AllOrdinaryIds(
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report)).Count);
    }

    [Fact]
    public void NegativeOrUnknownOemCountNeverProducesNegativeHeadline()
    {
        var report = new DriverSearchReport([], [Pass(Configured)]);
        var home = Home(report, oem: -4);

        Assert.Equal("Nessun aggiornamento proposto dalle fonti consultate",
            home.Headline);
        Assert.Contains("0 segnalati dal produttore", home.SourceLine);
        Assert.DoesNotContain("-4", home.Headline + home.SourceLine);
    }

    [Fact]
    public void OptionalSoftwareAndLocalCacheNeverInflateWuaHeadline()
    {
        var report = new DriverSearchReport([], [Pass(Configured)],
            RelatedSoftware: [
                new WuaDriverRelatedSoftware(
                    "00000000-0000-4000-8000-000000000555",
                    "Killer management extension",
                    "Windows configurato · componenti software")
            ],
            RelatedSoftwareSources: [
                Pass("Windows configurato · componenti software", count: 1)
            ]);
        var home = Home(report, oem: 1);
        Assert.Equal("Nessun candidato Windows Update dalle fonti consultate · " +
                     "1 segnalazione OEM da verificare", home.Headline);
        Assert.DoesNotContain("2 possibili", home.Headline);
    }
}
