using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #466: the optional Type=Software WUA source audit must fail closed on
/// structurally impossible legacy IPC statuses, without making a completed
/// independent Type=Driver pass fail or authorizing software installation.
/// </summary>
public sealed class WuaSoftwarePassStrictStatusTests
{
    private const string SoftwareOnline =
        "Windows Update online · componenti software";
    private const string SoftwareMu =
        "Microsoft Update online · componenti software";
    private const string SoftwareConfigured =
        "Windows configurato · componenti software";
    private const string GuidSoftware =
        "00000000-0000-4000-8000-000000000466";
    private const string GuidDriver =
        "00000000-0000-4000-8000-000000000467";
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";

    private static DriverSearchPassStatus Status(
        string name, string outcome, int candidates = 0,
        int added = 0) =>
        new(name, outcome, candidates, "PRIVATE_WSUS_SERIAL", added);

    private static WuaDriverRelatedSoftware Software(
        string source = SoftwareOnline) =>
        new(GuidSoftware, "Killer Networking Software", source);

    private static DriverUpdateInfo Driver() =>
        new(GuidDriver, "NVIDIA Display Driver", null, "Display",
            "Microsoft", null, null, false, "Microsoft", null,
            Nvidia, null, SearchSource: "Windows Update online");

    private static DriverSearchReport Report(
        IReadOnlyList<WuaDriverRelatedSoftware> software,
        params DriverSearchPassStatus[] softwareSources) =>
        new([Driver()],
            [Status("Windows Update online", "Completed", 1, 1)],
            software, softwareSources);

    [Theory]
    [InlineData("Failed", -1, 0)]
    [InlineData("Failed", 0, -1)]
    [InlineData("Failed", 0, 2)]
    [InlineData("Partial", -1, 0)]
    [InlineData("Partial", 1, 2)]
    [InlineData("NetworkError", 0, 1)]
    [InlineData("PolicyRestricted", 0, 1)]
    [InlineData("Completed", -1, 0)]
    [InlineData("Completed", 1, 2)]
    public void ImpossibleCountersAreUnverifiedForEverySoftwareOutcome(
        string outcome, int candidates, int added)
    {
        var bad = Status(SoftwareOnline, outcome, candidates, added);
        var report = Report([Software()], bad);

        Assert.False(WuaDriverRelatedSoftwareClassifier
            .IsVerifiableSoftwarePassStatus(bad, report.RelatedSoftwareSources!));
        Assert.True(WuaDriverRelatedSoftwareClassifier
            .HasUnverifiableSoftwarePassStatuses(report.RelatedSoftwareSources!));
        Assert.True(report.SoftwareSearchIsPartial);
        Assert.False(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report));
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        Assert.Equal(0, ReferenceDriverCoverage.Create([], report)[3]
            .SoftwareTitleHints);

        var copy = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains("fonte non verificata", copy);
        Assert.Contains("Verifica software OEM incompleta", copy);
        Assert.DoesNotContain("PRIVATE_", copy);
        Assert.DoesNotContain(GuidSoftware, copy);
        Assert.DoesNotContain("Killer Networking Software", copy);

        // The independent Type=Driver pass is not damaged by optional
        // Type=Software failures, even ones originating in corrupt IPC.
        Assert.False(report.IsPartial);
        Assert.Single(WindowsDriverCandidateMerge.ConfirmedDistinctForReport(
            report));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(report.Updates));
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Success")]
    [InlineData("NotAttempted")]
    [InlineData("")]
    public void UnknownSoftwareOutcomeCannotBeAnAuditedWuaSource(
        string outcome)
    {
        var pass = Status(SoftwareOnline, outcome, 0, 0);
        var report = Report([Software()], pass);

        Assert.False(WuaDriverRelatedSoftwareClassifier
            .IsVerifiableSoftwarePassStatus(pass, report.RelatedSoftwareSources!));
        Assert.True(report.SoftwareSearchIsPartial);
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        Assert.Contains("fonte non verificata",
            ScanCoverageText.Create(report, [], 0, 0, 0));
        Assert.False(report.IsPartial);
    }

    [Theory]
    [InlineData("PRIVATE_WSUS_PATH")]
    [InlineData(@"\\PRIVATE_SERVER\share")]
    [InlineData("Windows configurato · componenti software")]
    [InlineData("Windows Update online · componenti software")]
    public void ForeignOrWrongProviderNotRegisteredNeverMeansCompletedScan(
        string source)
    {
        var marker = Status(source, "NotRegistered");
        var report = Report([Software()], marker);

        Assert.False(WuaDriverRelatedSoftwareClassifier
            .IsVerifiableSoftwarePassStatus(marker, report.RelatedSoftwareSources!));
        Assert.True(WuaDriverRelatedSoftwareClassifier
            .HasUnverifiableSoftwarePassStatuses(report.RelatedSoftwareSources!));
        Assert.True(report.SoftwareSearchIsPartial);
        Assert.False(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report));
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));

        var copy = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains("Verifica software OEM incompleta", copy);
        Assert.DoesNotContain("PRIVATE_SERVER", copy);
        Assert.DoesNotContain("PRIVATE_WSUS_PATH", copy);
        Assert.DoesNotContain(GuidSoftware, copy);
        Assert.False(report.IsPartial);
    }

    [Theory]
    [InlineData(SoftwareMu)]
    [InlineData("Microsoft Update software")]
    public void ExplicitMicrosoftNotRegisteredLegacySentinelsStayNeutral(
        string source)
    {
        var report = Report([Software()],
            Status(source, "NotRegistered"));

        Assert.False(report.SoftwareSearchIsPartial);
        Assert.False(WuaDriverRelatedSoftwareClassifier
            .HasUnverifiableSoftwarePassStatuses(report.RelatedSoftwareSources!));
        Assert.False(WuaDriverRelatedSoftwareClassifier
            .HasCompletedKnownSoftwarePass(report));
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        Assert.True(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 1)]
    public void InvalidNotRegisteredMicrosoftCountersAreNotNeutral(
        int candidates, int added)
    {
        var report = Report([Software()],
            Status(SoftwareMu, "NotRegistered", candidates, added));
        Assert.True(report.SoftwareSearchIsPartial);
        Assert.True(WuaDriverRelatedSoftwareClassifier
            .HasUnverifiableSoftwarePassStatuses(report.RelatedSoftwareSources!));
        Assert.Empty(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
    }

    [Fact]
    public void CorruptIndependentSourceCannotInvalidateLegitimateSoftwareHint()
    {
        var bad = Status(SoftwareOnline, "NetworkError", -1, 0);
        var good = Status(SoftwareConfigured, "Completed", 1, 1);
        var report = Report([Software(SoftwareOnline),
            Software(SoftwareConfigured)], bad, good);

        Assert.True(report.SoftwareSearchIsPartial);
        Assert.True(WuaDriverRelatedSoftwareClassifier
            .IsVerifiableSoftwarePassStatus(good, report.RelatedSoftwareSources!));
        Assert.False(WuaDriverRelatedSoftwareClassifier
            .IsVerifiableSoftwarePassStatus(bad, report.RelatedSoftwareSources!));
        var validated = Assert.Single(
            WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(report));
        Assert.Equal(SoftwareConfigured, validated.SearchSource);
        Assert.Equal(GuidSoftware, validated.UpdateId);
        Assert.False(validated.IsInstallable);
        Assert.False(validated.IsDriverUpdate);

        var copy = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains("1 visualizzati da fonti verificate", copy);
        Assert.Contains("fonte non verificata", copy);
        Assert.Contains("Verifica software OEM incompleta", copy);
        Assert.DoesNotContain(GuidSoftware, copy);
        Assert.DoesNotContain("PRIVATE_", copy);
        Assert.False(report.IsPartial);
    }

    [Fact]
    public void ValidCompletedAndFailedPassesRemainIndependent()
    {
        var complete = Report([Software()],
            Status(SoftwareOnline, "Completed", 1, 1),
            Status(SoftwareMu, "Failed", 0, 0));

        Assert.True(complete.SoftwareSearchIsPartial); // failed secondary source
        Assert.Single(WuaDriverRelatedSoftwareClassifier
            .ConfirmedForReport(complete));
        Assert.True(WuaDriverRelatedSoftwareClassifier
            .IsVerifiableSoftwarePassStatus(
                complete.RelatedSoftwareSources![1],
                complete.RelatedSoftwareSources));
        Assert.False(WuaDriverRelatedSoftwareClassifier
            .HasUnverifiableSoftwarePassStatuses(complete.RelatedSoftwareSources));

        var healthy = Report([Software()],
            Status(SoftwareOnline, "Completed", 1, 1));
        Assert.False(healthy.SoftwareSearchIsPartial);
        Assert.Single(WuaDriverRelatedSoftwareClassifier.ConfirmedForReport(healthy));
        Assert.False(healthy.IsPartial);
    }
}
