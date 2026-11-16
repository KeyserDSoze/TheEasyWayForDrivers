using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #533: raw WUA Type=Driver COM counts must not be contradicted by
/// legacy/IPC rows from the SAME completed source. Other sources are
/// independent and WUA Type=Software never becomes installable.
/// </summary>
public sealed class WuaDriverSourceRawCountIntegrityTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string GpuGuid = "00000000-0000-4000-8000-000000000533";
    private const string KillerGuid = "00000000-0000-4000-8000-000000000534";

    private static DriverInfo Device(string id, string hardware,
        string kind) => new(id, "Generic Windows device",
            "Microsoft", kind, "Microsoft", "1.0.0.0", null,
            "oem12.inf", true, true, 0, [hardware], []);

    private static DriverUpdateInfo Offer(string guid, string hardware,
        string source = "Windows Update online") =>
        new(guid, "Windows driver", null, "Display", "Microsoft", null,
            null, false, "Microsoft", null, hardware, null,
            SearchSource: source);

    private static DriverSearchPassStatus Pass(string source, int raw,
        string outcome = "Completed") =>
        new(source, outcome, raw, "PRIVATE_WSUS_PATH");

    private static DriverSearchReport Report(
        IReadOnlyList<DriverUpdateInfo> offers,
        params DriverSearchPassStatus[] sources) => new(offers, sources);

    private static readonly DriverInfo Gpu =
        Device("PRIVATE_GPU_SERIAL", Nvidia, "Display");
    private static readonly DriverInfo Wifi =
        Device("PRIVATE_KILLER_SERIAL", Killer, "Net");

    [Fact]
    public void ZeroComRowsCannotAuthenticatePhantomOrdinaryDriver()
    {
        var report = Report([Offer(GpuGuid, Nvidia)],
            Pass("Windows Update online", 0));

        Assert.True(report.IsPartial);
        Assert.True(report.AllFailed);
        Assert.False(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report));
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(
            WindowsDriverCandidateMerge.ConfirmedForReport(report)));
        Assert.Equal(0, ReferenceDriverCoverage.Create([Gpu], report)[5]
            .WindowsDriverCandidates);
        Assert.Equal(0, WuaPnpAssociationAudit.Create([Gpu], report, true)
            .ConfirmedGuids);

        var home = SimpleScanPresentation.Create(report, true, 1, 0, 0, []);
        Assert.Contains("non completata", home.Headline);
        Assert.DoesNotContain("1 possibile aggiornamento", home.Headline);
        var status = string.Join(" ", WuaPassStatusPresentation.Create(report));
        Assert.Contains("NON verificata", status);
        Assert.DoesNotContain("ricerca completata", status);
        Assert.DoesNotContain("PRIVATE", status);
        var copy = ScanCoverageText.Create(report, [], 1, 0, 0,
            [Gpu], inventoryComplete: true);
        Assert.Contains("NON verificata", copy);
        Assert.DoesNotContain("1 candidati distinti", copy);
        Assert.DoesNotContain(GpuGuid, copy);
        Assert.DoesNotContain(Nvidia, copy);
        Assert.DoesNotContain("PRIVATE", copy);
    }

    [Fact]
    public void MoreOffersThanRawCountRejectsOnlyBadSource()
    {
        var report = Report([
            Offer(GpuGuid, Nvidia),
            Offer(KillerGuid, Killer),
            Offer(KillerGuid, Killer, "Windows configurato")
        ],
            Pass("Windows Update online", 1),
            Pass("Windows configurato", 1));

        Assert.True(report.IsPartial);
        Assert.False(report.AllFailed);
        var confirmed = Assert.Single(
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
        Assert.Equal(KillerGuid, confirmed.Id);
        Assert.Equal("Windows configurato", confirmed.SearchSource);
        Assert.Equal([KillerGuid], QuickUpdatePolicy.AllOrdinaryIds([confirmed]));
        var home = SimpleScanPresentation.Create(report, true, 2, 0, 0, []);
        Assert.Contains("Risultati parziali", home.Headline);
        Assert.Contains("1 possibile aggiornamento", home.Headline);
        var eight = ReferenceDriverCoverage.Create([Gpu, Wifi], report);
        Assert.Equal(0, eight[5].WindowsDriverCandidates);
        Assert.Equal(1, eight[4].WindowsDriverCandidates);
        var audit = WuaPnpAssociationAudit.Create([Gpu, Wifi], report, true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(1, audit.ExactHardware);
    }

    [Fact]
    public void TwoGenuineSourcesCanReportSameGuidIndependently()
    {
        var report = Report([
            Offer(GpuGuid, Nvidia, "Windows configurato"),
            Offer(GpuGuid, Killer, "Windows Update online")
        ],
            Pass("Windows configurato", 1),
            Pass("Windows Update online", 1));
        Assert.False(report.IsPartial);
        var confirmed = WindowsDriverCandidateMerge.ConfirmedForReport(report);
        Assert.Equal(2, confirmed.Count);
        Assert.Equal(1,
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report).Count);
        Assert.Equal(1, WuaPnpAssociationAudit.Create([Gpu, Wifi], report, true)
            .ConfirmedGuids);
    }

    [Fact]
    public void TwoOffersFromTwoRealComRowsStillAppear()
    {
        var report = Report([
            Offer(GpuGuid, Nvidia),
            Offer(KillerGuid, Killer)
        ], Pass("Windows Update online", 2));
        Assert.False(report.IsPartial);
        Assert.Equal(2,
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report).Count);
        Assert.Equal(2, WuaPnpAssociationAudit.Create([Gpu, Wifi], report, true)
            .ConfirmedGuids);
    }

    [Fact]
    public void FailedSourceNeverInvalidatesAnIndependentCompletedSource()
    {
        var report = Report([
            Offer(GpuGuid, Nvidia),
            Offer(KillerGuid, Killer, "Windows configurato")
        ], Pass("Windows Update online", 0, "NetworkError"),
            Pass("Windows configurato", 1));
        Assert.True(report.IsPartial);
        var verified = Assert.Single(
            WindowsDriverCandidateMerge.ConfirmedForReport(report));
        Assert.Equal(KillerGuid, verified.Id);
    }
}
