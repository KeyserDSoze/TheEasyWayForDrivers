using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Regression #403: source names returned by this helper are placed
/// directly in the Desktop's Home unavailable-control list.
/// </summary>
public sealed class WuaSourceFailurePolicySafetyTests
{
    private static DriverSearchPassStatus Status(
        string source, string outcome, int candidates = 0, int added = 0) =>
        new(source, outcome, candidates,
            @"C:\Users\PRIVATE_PERSON\PRIVATE_WSUS_DETAIL", added);

    [Fact]
    public void UnknownFailedAndCompletedSourceNamesNeverReachHomeErrorList()
    {
        var report = new DriverSearchReport([], [
            Status(@"\\PRIVATE-WSUS\PRIVATE_SERIAL", "Failed"),
            Status(@"C:\Users\PRIVATE_PERSON\PRIVATE.inf", "Completed", 20, 1),
            Status("Windows configurato", "Completed")
        ]);

        var unavailable = WuaSourceFailurePolicy.UnavailableSources(report);
        Assert.Equal([WuaSourceFailurePolicy.UnknownSourceLabel], unavailable);
        Assert.True(report.IsPartial);
        var home = SimpleScanPresentation.Create(report,
            inventoryComplete: true, inventoriedDevices: 2, problems: 0,
            oemRecommended: 0, unavailableSteps: unavailable);
        Assert.Contains("Alcuni controlli", home.Warning);

        var visible = string.Join(" ", unavailable.Select(item => "Windows Update: " + item));
        Assert.Contains("Fonte Windows non riconosciuta", visible);
        Assert.DoesNotContain("PRIVATE", visible);
        Assert.DoesNotContain("WSUS", visible);
        Assert.DoesNotContain("Users", visible);
    }

    [Fact]
    public void DuplicateCompletedPassesAreUnavailableEvenWithoutFailures()
    {
        var report = new DriverSearchReport([], [
            Status("Windows configurato", "Completed"),
            Status("Windows Update online", "Completed", 1, 1),
            Status("windows update ONLINE", "Completed", 1, 1)
        ]);

        Assert.True(report.IsPartial);
        Assert.Equal(["Windows Update online"],
            WuaSourceFailurePolicy.UnavailableSources(report));
        Assert.True(WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report));
        var lines = string.Join(" ", WuaPassStatusPresentation.Create(report));
        Assert.Contains("duplicati/contraddittori", lines);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, 2)]
    [InlineData(2, -1)]
    public void ImpossibleCompletedCountersStillReportSourceUnavailable(
        int count, int unique)
    {
        var report = new DriverSearchReport([], [
            Status("Windows configurato", "Completed"),
            Status("Windows Update online", "Completed", count, unique)
        ]);

        Assert.True(report.IsPartial);
        Assert.Equal(["Windows Update online"],
            WuaSourceFailurePolicy.UnavailableSources(report));
        Assert.Contains("NON verificata",
            string.Join(" ", WuaPassStatusPresentation.Create(report)));
    }

    [Theory]
    [InlineData("Windows Update online")]
    [InlineData("Windows configurato")]
    [InlineData("Microsoft Update online · avanzato")]
    public void InvalidNotRegisteredIsNotTreatedAsNeutral(string source)
    {
        var report = new DriverSearchReport([], [
            Status("Microsoft Update online", "NotRegistered"),
            Status(source, "NotRegistered")
        ]);

        Assert.True(report.IsPartial);
        Assert.Equal([source], WuaSourceFailurePolicy.UnavailableSources(report));
    }

    [Fact]
    public void ContradictoryMicrosoftRegistrationCannotSilenceAttemptFailure()
    {
        var report = new DriverSearchReport([], [
            Status("Windows configurato", "Completed"),
            Status("Microsoft Update online", "NotRegistered"),
            Status("MICROSOFT UPDATE ONLINE", "NetworkError")
        ]);

        Assert.True(report.IsPartial);
        Assert.Equal(["Microsoft Update online"],
            WuaSourceFailurePolicy.UnavailableSources(report));
    }

    [Fact]
    public void CanonicalNameAndSingleFailureArePreservedWithoutRawIpcCasing()
    {
        var report = new DriverSearchReport([], [
            Status("Windows configurato", "Completed", 0),
            Status("wINDOWS UPDATE ONLINE", "PolicyRestricted"),
            Status("Microsoft Update online", "NotRegistered")
        ]);

        Assert.True(report.IsPartial);
        Assert.Equal(["Windows Update online"],
            WuaSourceFailurePolicy.UnavailableSources(report));
        Assert.DoesNotContain("wINDOWS", string.Join(" ",
            WuaSourceFailurePolicy.UnavailableSources(report)));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("NetworkError")]
    [InlineData("Partial")]
    [InlineData("PolicyRestricted")]
    public void ValidKnownPassFailuresAreRetained(string outcome)
    {
        var report = new DriverSearchReport([], [
            Status("Windows configurato", "Completed"),
            Status("Windows Update online", outcome)
        ]);
        Assert.Equal(["Windows Update online"],
            WuaSourceFailurePolicy.UnavailableSources(report));
    }

    [Fact]
    public void TrulyCompletedZeroAndNeutralMicrosoftNotRegisteredAreNotUnavailable()
    {
        var report = new DriverSearchReport([], [
            Status("Windows configurato", "Completed"),
            Status("Windows Update online", "Completed"),
            Status("Microsoft Update online", "NotRegistered")
        ]);

        Assert.False(report.IsPartial);
        Assert.Empty(WuaSourceFailurePolicy.UnavailableSources(report));
    }

    [Fact]
    public void EmptyOrMissingReportDoesNotInventUnavailableSource()
    {
        Assert.Empty(WuaSourceFailurePolicy.UnavailableSources(null));
        Assert.Empty(WuaSourceFailurePolicy.UnavailableSources(
            new DriverSearchReport([], [])));
    }
}
