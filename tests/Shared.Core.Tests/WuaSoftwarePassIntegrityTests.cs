using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #415: Type=Software review-only evidence shares one strict per-pass
/// status gate across Home, 8-component matrix and anonymous QA copy.
/// </summary>
public sealed class WuaSoftwarePassIntegrityTests
{
    private const string Online = "Windows Update online · componenti software";
    private const string Configured = "Windows configurato · componenti software";
    private const string IntelId = "00000000-0000-4000-8000-000000000415";
    private const string KillerId = "00000000-0000-4000-8000-000000000416";

    private static DriverSearchPassStatus Pass(
        string source, string outcome = "Completed",
        int count = 1, int added = 0) =>
        new(source, outcome, count, "PRIVATE_WSUS_DETAIL", added);

    private static WuaDriverRelatedSoftware Hint(
        string id = KillerId, string source = Online) =>
        new(id, "Killer Networking Software", source);

    private static DriverSearchReport Report(
        IReadOnlyList<WuaDriverRelatedSoftware> hints,
        params DriverSearchPassStatus[] softwarePasses) =>
        new([], [Pass("Windows configurato")], hints, softwarePasses);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    public void ImpossibleCompletedCountersDoNotValidateSoftwareHints(
        int count, int added)
    {
        var bad = Pass(Online, count: count, added: added);
        var report = Report([Hint()], bad);

        Assert.False(WuaDriverRelatedSoftwareClassifier
            .IsVerifiableSoftwarePassStatus(bad, report.RelatedSoftwareSources!));
        Assert.False(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report));
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        Assert.True(report.SoftwareSearchIsPartial);
        Assert.Equal(0, ReferenceDriverCoverage.Create([], report)[3].SoftwareTitleHints);
        var copy = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains(Online + ": fonte non verificata", copy);
        Assert.DoesNotContain("interrogata, " + count + " Type=Software", copy);
        Assert.DoesNotContain("PRIVATE_", copy);
        Assert.Contains("0 visualizzati da fonti verificate", copy);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("NetworkError")]
    public void DuplicateOrContradictorySoftwarePassesArePartialAndNotQueried(
        string otherOutcome)
    {
        var report = Report([Hint()],
            Pass(Online), Pass(Online, otherOutcome));

        Assert.True(report.SoftwareSearchIsPartial);
        Assert.False(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report));
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));

        var copy = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Equal(2,
            copy.Split(Online + ": fonte non verificata").Length - 1);
        Assert.DoesNotContain("PRIVATE_", copy);
        Assert.Contains("Verifica software OEM incompleta", copy);
    }

    [Fact]
    public void ValidIndependentSourceStillShowsOnlyItsOwnSoftware()
    {
        var report = Report(
            [Hint(IntelId, Configured), Hint(KillerId, Online)],
            Pass(Configured), Pass(Online, count: 1, added: 4));

        Assert.True(report.SoftwareSearchIsPartial);
        Assert.True(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report));
        var trusted = Assert.Single(WuaDriverRelatedSoftwareClassifier
            .ConfirmedForReport(report));
        Assert.Equal(IntelId, trusted.UpdateId);
        Assert.Equal(Configured, trusted.SearchSource);
        Assert.False(trusted.IsInstallable);

        var copy = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains(Configured + ": interrogata, 1 Type=Software", copy);
        Assert.Contains(Online + ": fonte non verificata", copy);
        Assert.Contains("1 visualizzati da fonti verificate", copy);
        Assert.DoesNotContain("PRIVATE_", copy);
    }

    [Fact]
    public void UnknownCompletedSoftwareSourceDoesNotProveAQuery()
    {
        var report = Report(
            [Hint(KillerId, "PRIVATE_OEM_SOURCE")],
            Pass("PRIVATE_OEM_SOURCE"));

        Assert.True(report.SoftwareSearchIsPartial);
        Assert.False(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report));
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        var copied = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains("Fonte software Windows non riconosciuta: fonte non verificata",
            copied);
        Assert.DoesNotContain("PRIVATE_", copied);
    }

    [Theory]
    [InlineData("Microsoft Update online · componenti software")]
    [InlineData("Microsoft Update software")]
    public void NotRegisteredLegacyMarkerIsNeutralButNeverValidatesSoftware(
        string source)
    {
        var report = Report([Hint()],
            Pass(source, "NotRegistered", count: 0));
        Assert.False(report.SoftwareSearchIsPartial);
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        Assert.False(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(-1, 0)]
    public void NonzeroNotRegisteredCountersAreStillInvalid(
        int candidates, int added)
    {
        var report = Report([Hint()], Pass("Microsoft Update software",
            "NotRegistered", candidates, added));
        Assert.True(report.SoftwareSearchIsPartial);
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
    }

    [Fact]
    public void GenuineCompletedSoftwareSourceRemainsReviewOnly()
    {
        var report = Report([Hint()],
            Pass(Online));
        Assert.False(report.SoftwareSearchIsPartial);
        var verified = Assert.Single(WuaDriverRelatedSoftwareClassifier
            .ConfirmedForReport(report));
        Assert.Equal(KillerId, verified.UpdateId);
        Assert.False(verified.IsDriverUpdate);
        Assert.False(verified.IsInstallable);
        Assert.Contains(Online + ": interrogata, 1 Type=Software",
            ScanCoverageText.Create(report, [], 0, 0, 0));
    }
}
