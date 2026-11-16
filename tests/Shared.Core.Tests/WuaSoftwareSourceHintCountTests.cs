using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #527: a WUA Type=Software advisory cannot exceed its own completed
/// source's raw COM count, nor inherit trust from a case-variant source.
/// Never authorizes driver or software installation.
/// </summary>
public sealed class WuaSoftwareSourceHintCountTests
{
    private const string Online = "Windows Update online · componenti software";
    private const string Configured = "Windows configurato · componenti software";
    private const string NvidiaId = "00000000-0000-4000-8000-000000000527";
    private const string KillerId = "00000000-0000-4000-8000-000000000528";
    private const string IntelId = "00000000-0000-4000-8000-000000000529";

    private static DriverSearchPassStatus Status(
        string source, int rawCount = 1, string outcome = "Completed") =>
        new(source, outcome, rawCount, "PRIVATE_WSUS_DETAIL");

    private static WuaDriverRelatedSoftware Hint(
        string id, string source, string title = "NVIDIA Display Extension") =>
        new(id, title, source);

    private static DriverSearchReport Report(
        IReadOnlyList<WuaDriverRelatedSoftware> hints,
        params DriverSearchPassStatus[] passes) =>
        new([], [Status("Windows Update online", 0)], hints, passes);

    [Fact]
    public void CompletedSourceReturningZeroRowsCannotAuthenticateHint()
    {
        var report = Report(
            [Hint(NvidiaId, Online)],
            Status(Online, rawCount: 0));

        Assert.True(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report)); // actual query completed
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        Assert.Equal(0, ReferenceDriverCoverage.Create([], report)[2]
            .SoftwareTitleHints);
        var copy = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains("0 visualizzati da fonti verificate", copy);
        Assert.DoesNotContain(NvidiaId, copy);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(report.Updates));
    }

    [Fact]
    public void ExcessSourceHintsAreRefusedWithoutPoisoningHealthyPass()
    {
        var report = Report(
        [
            Hint(NvidiaId, Online),
            Hint(KillerId, Online, "Killer Networking Software"),
            Hint(IntelId, Configured, "Intel Wireless Extension")
        ],
        Status(Online, rawCount: 1),
        Status(Configured, rawCount: 1));

        var hint = Assert.Single(WuaDriverRelatedSoftwareClassifier
            .ConfirmedForReport(report));
        Assert.Equal(IntelId, hint.UpdateId);
        Assert.Equal(Configured, hint.SearchSource);
        Assert.False(hint.IsDriverUpdate);
        Assert.False(hint.IsInstallable);
        Assert.Equal(1, ReferenceDriverCoverage.Create([], report)[2]
            .SoftwareTitleHints);
    }

    [Fact]
    public void CaseVariantDuplicatePassCannotAuthenticateFirstCompletion()
    {
        var altered = Online.ToUpperInvariant();
        var good = Status(Online, rawCount: 1);
        var bad = Status(altered, rawCount: 1, outcome: "Failed");
        var report = Report([Hint(NvidiaId, Online)], good, bad);

        Assert.False(WuaDriverRelatedSoftwareClassifier
            .IsVerifiableSoftwarePassStatus(good, report.RelatedSoftwareSources!));
        Assert.True(WuaDriverRelatedSoftwareClassifier
            .HasUnverifiableSoftwarePassStatuses(report.RelatedSoftwareSources!));
        Assert.True(report.SoftwareSearchIsPartial);
        Assert.False(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report));
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        Assert.Contains("fonte non verificata",
            ScanCoverageText.Create(report, [], 0, 0, 0));
    }

    [Fact]
    public void DuplicateCaseVariantDoesNotInvalidateDifferentCompletedSource()
    {
        var report = Report(
        [
            Hint(NvidiaId, Online),
            Hint(IntelId, Configured, "Intel Wireless Extension")
        ],
        Status(Online),
        Status(Online.ToLowerInvariant(), outcome: "Failed"),
        Status(Configured));

        var hints = WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report);
        var valid = Assert.Single(hints);
        Assert.Equal(IntelId, valid.UpdateId);
        Assert.Equal(Configured, valid.SearchSource);
        Assert.True(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report));
    }

    [Fact]
    public void TwoRowsFromTwoRawResultsRemainReviewOnly()
    {
        var report = Report(
        [
            Hint(NvidiaId, Online),
            Hint(KillerId, Online, "Killer Networking Software")
        ],
        Status(Online, rawCount: 2));

        var hints = WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report);
        Assert.Equal(2, hints.Count);
        Assert.All(hints, hint =>
        {
            Assert.False(hint.IsInstallable);
            Assert.False(hint.IsDriverUpdate);
        });
    }
}
