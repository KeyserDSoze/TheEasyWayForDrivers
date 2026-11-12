using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// WUA Type=Software is review-only. An IPC snapshot must not show a
/// software hint from a failed or unrecognized source, even when a
/// separate Type=Driver or Type=Software query completed.
/// </summary>
public sealed class WuaSoftwareCompletedSourceProvenanceTests
{
    private const string Configured =
        "Windows configurato · componenti software";
    private const string Online =
        "Windows Update online · componenti software";
    private const string Mu =
        "Microsoft Update online · componenti software";
    private const string IntelId =
        "00000000-0000-4000-8000-000000000376";
    private const string KillerId =
        "00000000-0000-4000-8000-000000000377";

    private static WuaDriverRelatedSoftware Hint(
        string id = KillerId,
        string title = "Killer Networking Software",
        string source = Online) => new(id, title, source);

    private static DriverSearchPassStatus Pass(
        string name, string outcome = "Completed") =>
        new(name, outcome, 1, "PRIVATE_WSUS_USER_ORDER");

    private static DriverSearchReport Report(
        IReadOnlyList<WuaDriverRelatedSoftware> hints,
        params DriverSearchPassStatus[] sources) =>
        new([], [Pass("Windows Update online")], hints, sources);

    [Theory]
    [InlineData("Failed")]
    [InlineData("NetworkError")]
    [InlineData("PolicyRestricted")]
    [InlineData("Partial")]
    [InlineData("NotRegistered")]
    public void ACompletedOtherPassCannotVerifyFailedKillerSoftware(
        string failedOutcome)
    {
        var report = Report(
            [Hint()],
            Pass(Configured), Pass(Online, failedOutcome));
        Assert.True(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report));
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        Assert.Equal(0, ReferenceDriverCoverage.Create([], report)[3].SoftwareTitleHints);

        var presentation = Assert.Single(ReferenceCoveragePresentation
            .Create([], report, true),
            row => row.Component == "Killer · Networking Software");
        Assert.Contains("0 titoli Software WUA", presentation.SoftwareStatus);
        Assert.DoesNotContain("Possibile componente software", presentation.NextStep);
        Assert.False(presentation.IsInstallable);

        var copied = ScanCoverageText.Create(report, [], 0, 0, 0, [],
            inventoryComplete: true);
        Assert.Contains("0 visualizzati da fonti verificate", copied);
        Assert.Contains("0 titoli WUA Type=Software", copied);
        Assert.DoesNotContain("PRIVATE_", copied);
        Assert.DoesNotContain(KillerId, copied);
        Assert.DoesNotContain("Killer Networking Software", copied);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(report.Updates));
    }

    [Fact]
    public void FirstBadThenGoodSameGuidKeepsOnlySuccessfulSourceAndTitle()
    {
        var guid = Guid.Parse(IntelId);
        var report = Report(
        [
            Hint(guid.ToString("B"), "NVIDIA Platform Controller", Online),
            Hint(guid.ToString("N"), "Intel PROSet Wireless Extension", Mu),
            Hint(guid.ToString("P"), "Intel PROSet Wireless Extension", Mu)
        ], Pass(Configured), Pass(Online, "Failed"), Pass(Mu));

        var verified = Assert.Single(
            WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        Assert.Equal(IntelId, verified.UpdateId);
        Assert.Equal(Mu, verified.SearchSource);
        Assert.Equal("Intel PROSet Wireless Extension", verified.Title);
        Assert.False(verified.IsInstallable);
        Assert.False(verified.IsDriverUpdate);
        var rows = ReferenceDriverCoverage.Create([], report);
        Assert.Equal(1, rows[2].SoftwareTitleHints);
        Assert.Equal(0, rows[7].SoftwareTitleHints);
        Assert.Equal(0, rows.Sum(row => row.WindowsDriverCandidates));
    }

    [Fact]
    public void SpoofedSoftwareSourceAndUnsafeLegacyTitleAreRejected()
    {
        var report = Report(
        [
            Hint(source: "PRIVATE_OEM_CATALOG"),
            Hint("PRIVATE_GUID", "Intel Wireless Extension", Configured),
            Hint(IntelId, "NVIDIA BIOS Firmware Update", Configured),
            Hint(KillerId, @"Intel Wireless Extension C:\PRIVATE_ORDER", Configured),
            Hint(IntelId, "Intel Wireless Extension", Configured)
        ], Pass(Configured), Pass("PRIVATE_OEM_CATALOG"));

        var one = Assert.Single(
            WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        Assert.Equal(IntelId, one.UpdateId);
        Assert.Equal(Configured, one.SearchSource);
        Assert.Equal(1, ReferenceDriverCoverage.Create([], report)[2].SoftwareTitleHints);
        Assert.False(one.IsInstallable);
        var copy = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.DoesNotContain("PRIVATE", copy);
        Assert.DoesNotContain(IntelId, copy);
    }

    [Fact]
    public void UnknownCompletedSoftwareOrDriverPassCannotConfirmHints()
    {
        var report = new DriverSearchReport([],
            [Pass("Windows Update online")],
            [Hint()],
            [Pass("PRIVATE_UNKNOWN_SOURCE"), Pass(Online, "NetworkError")]);

        Assert.False(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report));
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        var killer = ReferenceCoveragePresentation.Create([], report, true)[3];
        Assert.Contains("non verificata", killer.SoftwareStatus);
        var copy = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains("0 visualizzati da fonti verificate", copy);
        Assert.DoesNotContain("PRIVATE_", copy);
    }

    [Fact]
    public void ContradictoryDuplicateStatusesForSameSoftwareSourceFailClosed()
    {
        var report = Report([Hint()],
            Pass(Online), Pass(Online, "NetworkError"));
        Assert.False(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report));
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        Assert.Equal(0, ReferenceDriverCoverage.Create([], report)[3].SoftwareTitleHints);
    }

    [Fact]
    public void ValidSoftwareGuidCanBeShownEvenWhenDriverSearchFails()
    {
        var report = new DriverSearchReport([],
            [Pass("Windows Update online", "Failed")],
            [Hint()],
            [Pass(Online)]);
        Assert.False(report.SoftwareSearchIsPartial);
        Assert.Single(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        var row = ReferenceCoveragePresentation.Create([], report, true)[3];
        Assert.Contains("1 titoli Software WUA", row.SoftwareStatus);
        Assert.Contains("Non è un candidato driver", row.WindowsDriverStatus);
        Assert.False(row.IsInstallable);
    }

    [Fact]
    public void LegacySnapshotBeyond32KeepsLateNvidiaAndKillerFairly()
    {
        var first = Enumerable.Range(1, 80)
            .Select(i => Hint($"00000000-0000-4000-8000-{i:X12}",
                "Intel PROSet Wireless Extension", Configured));
        var late = new[]
        {
            Hint(KillerId, "Killer Networking Software", Mu),
            Hint(IntelId, "NVIDIA Platform Controller", Mu)
        };
        var report = Report(first.Concat(late).ToArray(),
            Pass(Configured), Pass(Mu));
        var confirmed = WuaDriverRelatedSoftwareClassifier
            .ConfirmedForReport(report);
        Assert.Equal(WuaDriverRelatedSoftwareClassifier.MaxItems,
            confirmed.Count);
        Assert.Contains(confirmed, hint => hint.UpdateId == KillerId);
        Assert.Contains(confirmed, hint => hint.UpdateId == IntelId);
        Assert.All(confirmed, hint => Assert.False(hint.IsInstallable));
    }
}
