using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #474: WUA-only re-search must replace stale WUA warnings without
/// resetting inventory/OEM failures or exporting private IPC source names.
/// </summary>
public sealed class GuidedWuaUnavailableStepsTests
{
    private static DriverSearchReport Completed() =>
        new([], [
            new DriverSearchPassStatus("Windows configurato",
                "Completed", 0, "OK", 0),
            WindowsDriverSearchPlan.NotRegisteredStatus()
        ]);

    [Fact]
    public void SuccessfulRescanClearsStaleWindowsFailuresOnly()
    {
        var previous = new[]
        {
            "inventario dispositivi", "ricerca Windows Update",
            "Windows Update: Windows configurato",
            "Windows Update: Microsoft Update online",
            AutomaticOemCatalogCoverage.UnavailableStep,
            "riconoscimento fonti OEM"
        };

        var refreshed = GuidedWuaUnavailableSteps.WithLatestReport(
            previous, Completed());

        Assert.Equal([
            "inventario dispositivi",
            AutomaticOemCatalogCoverage.UnavailableStep,
            "riconoscimento fonti OEM"
        ], refreshed);
    }

    [Fact]
    public void FailedSourceIsAddedAfterPreviouslyCompleteWuaScan()
    {
        var partial = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows configurato",
                "Completed", 0, "OK", 0),
            new DriverSearchPassStatus("Windows Update online",
                "NetworkError", 0, "private hostname", 0)
        ]);

        var steps = GuidedWuaUnavailableSteps.WithLatestReport(
            [AutomaticOemCatalogCoverage.UnavailableStep], partial);

        Assert.Equal([
            AutomaticOemCatalogCoverage.UnavailableStep,
            "Windows Update: Windows Update online"
        ], steps);
        Assert.True(partial.IsPartial);
    }

    [Fact]
    public void AllFailedRescanReportsUnverifiedWuaNotZeroUpdates()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows configurato",
                "Failed", 0, "internal path", 0)
        ]);

        var steps = GuidedWuaUnavailableSteps.WithLatestReport(
            [], report);
        Assert.Equal([
            GuidedWuaUnavailableSteps.SearchStep,
            "Windows Update: Windows configurato"
        ], steps);
    }

    [Fact]
    public void MissingSourcesAreAlsoIncompleteAndNotSilentlySuccessful()
    {
        var emptyReport = new DriverSearchReport([], []);
        Assert.Equal([GuidedWuaUnavailableSteps.SearchStep],
            GuidedWuaUnavailableSteps.WithLatestReport(
                ["Windows Update: Windows Update online"], emptyReport));
        Assert.Equal([GuidedWuaUnavailableSteps.SearchStep],
            GuidedWuaUnavailableSteps.WithLatestReport([], null));
    }

    [Fact]
    public void UnknownIpcSourceCannotLeakPrivateIdentifierToHome()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows configurato",
                "Completed", 0, "OK", 0),
            new DriverSearchPassStatus("PRIVATE_WSUS_HOSTNAME",
                "Failed", 0, "PRIVATE_MACHINE", 0)
        ]);

        var steps = GuidedWuaUnavailableSteps.WithLatestReport(
            [], report);
        Assert.Equal([
            "Windows Update: " + WuaSourceFailurePolicy.UnknownSourceLabel
        ], steps);
        Assert.DoesNotContain("PRIVATE_", string.Join(" ", steps));
    }

    [Fact]
    public void NotRegisteredAndSoftwareFailureDoNotPoisonDriverCoverage()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows configurato",
                "Completed", 0, "OK", 0),
            WindowsDriverSearchPlan.NotRegisteredStatus()
        ], RelatedSoftwareSources: [
            new DriverSearchPassStatus(
                "Windows Update online · componenti software",
                "Failed", 0, "Offline", 0)
        ]);

        Assert.Empty(GuidedWuaUnavailableSteps.WithLatestReport(
            ["ricerca Windows Update"], report));
        Assert.True(report.SoftwareSearchIsPartial);
        Assert.False(report.IsPartial);
    }

    [Fact]
    public void RepeatedSearchReplacementDoesNotAccumulateOldWarnings()
    {
        var failed = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows Update online",
                "Failed", 0, "Offline", 0)
        ]);
        var first = GuidedWuaUnavailableSteps.WithLatestReport(
            ["inventario dispositivi", "ricerca Windows Update",
             "Windows Update: Windows configurato"], failed);
        var second = GuidedWuaUnavailableSteps.WithLatestReport(first, failed);
        Assert.Equal(first, second);

        var recovered = GuidedWuaUnavailableSteps.WithLatestReport(
            second, Completed());
        Assert.Equal(["inventario dispositivi"], recovered);
    }

    [Fact]
    public void NullPreviousCollectionIsNotAccepted() =>
        Assert.Throws<ArgumentNullException>(() =>
            GuidedWuaUnavailableSteps.WithLatestReport(null!, Completed()));
}
