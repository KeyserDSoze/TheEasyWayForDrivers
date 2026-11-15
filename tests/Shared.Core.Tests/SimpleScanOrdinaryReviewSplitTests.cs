using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #469: a Home result count must not be read as an installable update
/// count when all completed WUA passes returned only firmware, optional,
/// advanced or otherwise non-ordinary Type=Driver GUIDs.
/// </summary>
public sealed class SimpleScanOrdinaryReviewSplitTests
{
    private const string OrdinarySource = "Windows Update online";
    private const string ConfiguredSource = "Windows configurato";
    private const string AdvancedSource = "Windows Update online · avanzato";
    private const string MicrosoftSource = "Microsoft Update online";

    private static string GuidId(int n) =>
        $"00000000-0000-4000-8000-{n:D12}";

    private static DriverUpdateInfo Offer(
        int id, string title = "NVIDIA Display Driver",
        string source = OrdinarySource,
        bool optional = false, bool advanced = false, bool hidden = false,
        string cls = "Display") =>
        new(GuidId(id), title, null, cls, "Microsoft", null,
            null, false, "Microsoft", null,
            @"PCI\VEN_10DE&DEV_28A0", null,
            IsHidden: hidden, IsOptional: optional,
            IsAdvancedCandidate: advanced, SearchSource: source);

    private static DriverSearchPassStatus Pass(
        string source = OrdinarySource,
        string outcome = "Completed", int count = 5) =>
        new(source, outcome, count, "PRIVATE_WSUS_ENDPOINT",
            outcome == "Completed" ? count : 0);

    private static SimpleScanPresentation Home(
        DriverSearchReport report, int oemApplicable = 0) =>
        SimpleScanPresentation.Create(report, true, 8, 0, oemApplicable, []);

    [Fact]
    public void FirmwareOptionalAndAdvancedOnlyAreReviewNotInstallableHeadline()
    {
        var offers = new[]
        {
            Offer(1, "NVIDIA GPU Firmware", cls: "Firmware"),
            Offer(2, "Intel Wireless Driver", optional: true),
            Offer(3, "Realtek Audio Extension",
                AdvancedSource, advanced: true)
        };
        var report = new DriverSearchReport(offers,
            [Pass(), Pass(AdvancedSource)]);
        var summary = Home(report);

        Assert.False(report.IsPartial);
        Assert.Equal("3 risultati Windows Update da verificare",
            summary.Headline);
        Assert.Contains("3 candidati Windows Update", summary.SourceLine);
        Assert.Contains("0 ordinari selezionabili", summary.SourceLine);
        Assert.Contains("3 da verificare", summary.SourceLine);
        Assert.Contains("nessuna installazione senza nuova verifica Windows",
            summary.SourceLine);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report)));
        Assert.DoesNotContain("possibili aggiornamenti rilevati", summary.Headline);
        Assert.DoesNotContain("PRIVATE_", summary.SourceLine);
    }

    [Fact]
    public void MixedOrdinaryAndReviewKeepOriginalHeadlineWithAccurateBreakdown()
    {
        var report = new DriverSearchReport(
        [
            Offer(1),
            Offer(2, "NVIDIA GPU Firmware"),
            Offer(3, "Realtek audio driver", optional: true),
            Offer(4, "Intel network extension",
                AdvancedSource, advanced: true)
        ],
        [Pass(), Pass(AdvancedSource)]);
        var summary = Home(report);

        Assert.Equal("4 possibili aggiornamenti rilevati", summary.Headline);
        Assert.Contains("4 candidati Windows Update", summary.SourceLine);
        Assert.Contains("1 ordinari selezionabili", summary.SourceLine);
        Assert.Contains("3 da verificare", summary.SourceLine);
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report)));
        Assert.DoesNotContain("PRIVATE_", summary.SourceLine);
    }

    [Fact]
    public void SingleOptionalCandidateIsNotAnApprovedDriverUpdate()
    {
        var report = new DriverSearchReport(
            [Offer(1, optional: true)], [Pass()]);
        var result = Home(report);

        Assert.Equal("1 risultato Windows Update da verificare",
            result.Headline);
        Assert.Contains("1 candidati Windows Update", result.SourceLine);
        Assert.Contains("0 ordinari selezionabili", result.SourceLine);
        Assert.Contains("1 da verificare", result.SourceLine);
        Assert.DoesNotContain("1 possibile aggiornamento rilevato",
            result.Headline);
    }

    [Fact]
    public void TrustedFirmwareFromSecondPassVetoesSameGuidOrdinaryOffer()
    {
        var baseOffer = Offer(1, source: ConfiguredSource);
        var firmware = Offer(1, "NVIDIA Firmware",
            source: MicrosoftSource) with
        {
            Id = Guid.Parse(baseOffer.Id).ToString("B")
        };
        var report = new DriverSearchReport([baseOffer, firmware],
            [Pass(ConfiguredSource), Pass(MicrosoftSource)]);
        var confirmed = Assert.Single(
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
        Assert.True(QuickUpdatePolicy.IsFirmwareOrBios(
            confirmed.Title, confirmed.DriverClass));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds([confirmed]));
        var summary = Home(report);

        Assert.Equal("1 risultato Windows Update da verificare",
            summary.Headline);
        Assert.Contains("1 candidati Windows Update", summary.SourceLine);
        Assert.Contains("0 ordinari selezionabili", summary.SourceLine);
        Assert.Contains("1 da verificare", summary.SourceLine);
        Assert.DoesNotContain(baseOffer.Id, summary.SourceLine);
    }

    [Fact]
    public void TrustedBrowseOnlyFromSecondPassVetoesSameGuidOrdinaryOffer()
    {
        var baseOffer = Offer(1, source: ConfiguredSource);
        var browseOnly = Offer(1, source: MicrosoftSource, optional: true)
            with { Id = Guid.Parse(baseOffer.Id).ToString("N") };
        var report = new DriverSearchReport([baseOffer, browseOnly],
            [Pass(ConfiguredSource), Pass(MicrosoftSource)]);
        var confirmed = Assert.Single(
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
        Assert.True(confirmed.IsOptional);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds([confirmed]));
        var summary = Home(report);
        Assert.Equal("1 risultato Windows Update da verificare",
            summary.Headline);
        Assert.Contains("0 ordinari selezionabili", summary.SourceLine);
    }

    [Fact]
    public void FailedAndUnknownSourcesCannotInflateOrVetoCompletedGuid()
    {
        var valid = Offer(1, source: ConfiguredSource);
        var failed = Offer(1, "UEFI Firmware", source: MicrosoftSource);
        var unknown = Offer(2, source: @"\\PRIVATE_WSUS\order");
        var report = new DriverSearchReport(
            [valid, failed, unknown],
            [Pass(ConfiguredSource), Pass(MicrosoftSource, "NetworkError", 0),
             Pass(@"\\PRIVATE_WSUS\order", "Completed", 1)]);
        var summary = Home(report);

        Assert.True(report.IsPartial);
        Assert.Equal("Risultati parziali: 1 possibile aggiornamento rilevato",
            summary.Headline);
        Assert.Contains("1 candidati Windows Update", summary.SourceLine);
        Assert.Contains("1 ordinari selezionabili", summary.SourceLine);
        Assert.Contains("0 da verificare", summary.SourceLine);
        Assert.Contains("copertura PARZIALE", summary.SourceLine);
        Assert.DoesNotContain("PRIVATE_", summary.Headline + summary.SourceLine);
        Assert.Single(WindowsDriverCandidateMerge
            .ConfirmedDistinctForReport(report));
    }

    [Fact]
    public void AdvancedOnlyFromCompletedSourceIsPartialWhenOtherPassFails()
    {
        var report = new DriverSearchReport(
            [Offer(1, "NVIDIA Display Extension",
                source: AdvancedSource, advanced: true)],
            [Pass(AdvancedSource),
             Pass(OrdinarySource, "Failed", 0)]);
        var summary = Home(report);

        Assert.True(report.IsPartial);
        Assert.Equal("Risultati parziali: 1 risultato Windows Update da verificare",
            summary.Headline);
        Assert.Contains("0 ordinari selezionabili", summary.SourceLine);
        Assert.Contains("1 da verificare", summary.SourceLine);
        Assert.Contains("solo fonti riuscite, copertura PARZIALE",
            summary.SourceLine);
    }

    [Fact]
    public void ActualManufacturerAssessedPackagesRemainSeparateFromWuaReview()
    {
        var report = new DriverSearchReport(
            [Offer(1, "Intel UEFI Capsule")], [Pass()]);
        var summary = Home(report, oemApplicable: 2);

        Assert.Equal("3 possibili aggiornamenti rilevati", summary.Headline);
        Assert.Contains("1 candidati Windows Update", summary.SourceLine);
        Assert.Contains("0 ordinari selezionabili", summary.SourceLine);
        Assert.Contains("1 da verificare", summary.SourceLine);
        Assert.Contains("2 segnalati dal produttore", summary.SourceLine);
        Assert.Contains("non sono aggiornamenti", summary.SourceLine);
    }

    [Fact]
    public void EmptyCompleteSourceHasNoMisleadingOptionalBreakdown()
    {
        var report = new DriverSearchReport([], [Pass(count: 0)]);
        var summary = Home(report);

        Assert.Equal("Nessun aggiornamento proposto dalle fonti consultate",
            summary.Headline);
        Assert.Contains("0 candidati Windows Update", summary.SourceLine);
        Assert.DoesNotContain("ordinari selezionabili", summary.SourceLine);
        Assert.DoesNotContain("da verificare", summary.SourceLine);
    }

    [Fact]
    public void OptionalSoftwareAndHistoricalCacheNeverEnterDriverCounts()
    {
        var report = new DriverSearchReport([], [Pass(count: 0)],
            RelatedSoftware: [
                new WuaDriverRelatedSoftware(GuidId(2),
                    "Intel PROSet Wireless Extension",
                    "Windows Update online · componenti software")
            ],
            RelatedSoftwareSources: [
                Pass("Windows Update online · componenti software", count: 1)
            ],
            CachedDriverHints: []);
        var summary = Home(report);

        Assert.Equal("Nessun aggiornamento proposto dalle fonti consultate",
            summary.Headline);
        Assert.Contains("0 candidati Windows Update", summary.SourceLine);
        Assert.DoesNotContain("ordinari selezionabili", summary.SourceLine);
    }

    [Fact]
    public void WuaUnavailableNeverClaimsSelectableCandidatesEvenWithOem()
    {
        var report = new DriverSearchReport(
            [Offer(1)],
            [Pass(OrdinarySource, "Failed", count: 0)]);
        var summary = Home(report, oemApplicable: 2);

        Assert.Contains("Risultati parziali: 2 possibili aggiornamenti",
            summary.Headline);
        Assert.Contains("Windows Update non verificato", summary.SourceLine);
        Assert.DoesNotContain("ordinari selezionabili", summary.SourceLine);
        Assert.Contains("2 segnalati dal produttore", summary.SourceLine);
    }
}
