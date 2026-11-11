using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Each WUA Type=Driver GUID must have a successfully completed OWN pass.
/// Another completed source is not provenance for a failed/partial source,
/// even in a legacy IPC snapshot that supplied stale update rows.
/// </summary>
public sealed class WuaCompletedSourceProvenanceTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string GpuGuid = "00000000-0000-4000-8000-000000000373";
    private const string KillerGuid = "00000000-0000-4000-8000-000000000374";

    private static DriverInfo Device(string instance, string name,
        string type, string hardware) =>
        new(instance, name, "Microsoft", type, "Microsoft", "1.0.0.0",
            null, "oem123.inf", true, true, 0, [hardware], []);

    private static DriverUpdateInfo Offer(string id, string idHardware,
        string source = "Windows Update online",
        bool incomplete = false, bool advanced = false) =>
        new(id, "Untrusted title", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, idHardware, null,
            IsAdvancedCandidate: advanced, SearchSource: source,
            AdditionalHardwareIdsIncomplete: incomplete);

    private static DriverSearchPassStatus Pass(string source, string outcome) =>
        new(source, outcome, 1, "PRIVATE_PC_WSUS_USER_SERIAL");

    private static readonly DriverInfo NvidiaDevice =
        Device("PRIVATE_NVIDIA", "Microsoft Basic Display Adapter",
            "Display", Nvidia);
    private static readonly DriverInfo KillerDevice =
        Device("PRIVATE_KILLER", "Killer Wi-Fi", "Net", Killer);

    [Theory]
    [InlineData("Failed")]
    [InlineData("NetworkError")]
    [InlineData("PolicyRestricted")]
    [InlineData("Partial")]
    [InlineData("NotRegistered")]
    public void ACompletedUnrelatedPassCannotAuthenticateFailedSourceGuid(
        string outcome)
    {
        var report = new DriverSearchReport(
            [Offer(GpuGuid, Nvidia, incomplete: true)],
            [Pass("Windows configurato", "Completed"),
             Pass("Windows Update online", outcome)]);
        Assert.True(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report));
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedForReport(report));

        var home = HomeVendorCoverageSummary.Create([NvidiaDevice], report, true);
        Assert.Contains("0 candidati WUA", home[0].WindowsStatus);
        Assert.DoesNotContain("ID WUA incompleti", home[0].WindowsStatus);
        var eight = ReferenceDriverCoverage.Create([NvidiaDevice], report);
        Assert.Equal(0, eight[5].WindowsDriverCandidates);
        Assert.False(eight[5].WuaAssociationsIncomplete);
        var audit = WuaPnpAssociationAudit.Create([NvidiaDevice], report, true);
        Assert.Equal(0, audit.ConfirmedGuids);
        Assert.Equal(0, audit.IncompleteWuaIds);
        var summary = ScanCoverageText.Create(report, [], 1, 0, 0,
            [NvidiaDevice], inventoryComplete: true);
        Assert.Contains("Windows Update: 0 candidati distinti", summary);
        Assert.DoesNotContain("Candidati WUA con associazioni dispositivo incomplete",
            summary);
        Assert.DoesNotContain("PRIVATE_", summary);
        Assert.DoesNotContain(Nvidia, summary);
        Assert.DoesNotContain(GpuGuid, summary);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameGuidFromFailedSourceCannotSupplyExtraNvidiaIds(
        bool failedFirst)
    {
        var good = Offer(GpuGuid, Killer, "Windows configurato");
        var bad = Offer("{" + GpuGuid + "}", Nvidia,
            "Windows Update online", incomplete: true);
        var report = new DriverSearchReport(
            failedFirst ? [bad, good] : [good, bad],
            [Pass("Windows configurato", "Completed"),
             Pass("Windows Update online", "Failed")]);
        var approved = WindowsDriverCandidateMerge.ConfirmedForReport(report);
        var only = Assert.Single(approved);
        Assert.Equal(Killer, only.HardwareId);
        Assert.False(only.AdditionalHardwareIdsIncomplete);

        var audit = WuaPnpAssociationAudit.Create(
            [NvidiaDevice, KillerDevice], report, true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(0, audit.Unattributed);
        var matrix = ReferenceDriverCoverage.Create(
            [NvidiaDevice, KillerDevice], report);
        Assert.Equal(0, matrix[5].WindowsDriverCandidates);
        Assert.Equal(1, matrix[4].WindowsDriverCandidates);
    }

    [Fact]
    public void CompletedRealPassKeepsItsOwnNvidiaAndKillerEvidence()
    {
        var report = new DriverSearchReport(
            [Offer(GpuGuid, Nvidia, "Windows Update online"),
             Offer(KillerGuid, Killer, "Windows configurato")],
            [Pass("Windows configurato", "Completed"),
             Pass("Windows Update online", "Completed"),
             Pass("Microsoft Update online", "Failed")]);
        var confirmed = WindowsDriverCandidateMerge.ConfirmedForReport(report);
        Assert.Equal(2, confirmed.Count);
        Assert.Equal(2, WuaPnpAssociationAudit.Create(
            [NvidiaDevice, KillerDevice], report, true).ConfirmedGuids);
        var home = HomeVendorCoverageSummary.Create(
            [NvidiaDevice, KillerDevice], report, true);
        Assert.Contains("1 candidato WUA", home[0].WindowsStatus);
        Assert.Contains("1 candidato WUA", home[1].WindowsStatus);
    }

    [Fact]
    public void AdvancedAndSoftwarePassesCannotBorrowOrdinaryCompletion()
    {
        var report = new DriverSearchReport(
            [Offer(GpuGuid, Nvidia, "Windows Update online · avanzato",
                advanced: true)],
            [Pass("Windows Update online", "Completed"),
             Pass("Windows Update online · avanzato", "NetworkError")],
            RelatedSoftware:
            [new WuaDriverRelatedSoftware(KillerGuid,
                "Killer Networking Software",
                "Windows Update online · componenti software")],
            RelatedSoftwareSources:
            [Pass("Windows Update online · componenti software", "Completed")]);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedForReport(report));
        var matrix = ReferenceDriverCoverage.Create([NvidiaDevice], report);
        Assert.Equal(0, matrix[5].WindowsDriverCandidates);
        Assert.Equal(1, matrix[3].SoftwareTitleHints);
    }


    [Fact]
    public void FailedWuaSourceCannotEnterDesktopUpdateAllOrNotifications()
    {
        // Without the completed-source filter both rows independently
        // pass the ordinary WUA quick-update predicate.
        var report = new DriverSearchReport(
            [Offer(GpuGuid, Nvidia, "Windows Update online"),
             Offer(KillerGuid, Killer, "Windows configurato")],
            [Pass("Windows configurato", "Completed"),
             Pass("Windows Update online", "NetworkError")]);
        Assert.Equal(2, QuickUpdatePolicy.AllOrdinaryIds(report.Updates).Count);
        var display = WindowsDriverCandidateMerge.ConfirmedForReport(report);
        var verified = Assert.Single(display);
        Assert.Equal(KillerGuid, verified.Id);
        Assert.Equal(KillerGuid,
            Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(display)));
        Assert.True(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report));
        Assert.True(report.IsPartial);
    }

    [Fact]
    public void NoTrustedCompletedPassLeavesDesktopUpdatesEmpty()
    {
        var report = new DriverSearchReport(
            [Offer(GpuGuid, Nvidia, "Windows Update online")],
            [Pass("Windows Update online", "Failed"),
             Pass("PRIVATE_UNKNOWN_SOURCE", "Completed")]);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedForReport(report));
        Assert.False(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report));
    }

    [Fact]
    public void UnknownCompletedSourceCannotVerifyTypeDriverData()
    {
        var report = new DriverSearchReport(
            [Offer(GpuGuid, Nvidia)],
            [Pass("PRIVATE_FAKED_SOURCE", "Completed")]);
        Assert.False(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report));
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedForReport(report));
        Assert.False(WuaPnpAssociationAudit.Create(
            [NvidiaDevice], report, true).SearchVerified);
        var home = HomeVendorCoverageSummary.Create([NvidiaDevice], report, true);
        Assert.Contains("non verificata", home[0].WindowsStatus);
    }
}
