using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class ScanCoverageTextTests
{
    [Fact]
    public void ReportSeparatesQueriedMissingAndFailedSourcesWithoutPrivateIds()
    {
        var wua = new DriverSearchReport(
            [
                new DriverUpdateInfo("71906b24-4e3a-46ae-8d99-7352d941eeb1",
                    "NVIDIA Display", null, null, "NVIDIA", null, null,
                    false, null, null, @"PCI\VEN_10DE&DEV_28A0", null)
            ],
            [
                new DriverSearchPassStatus("Windows Update online", "Completed",
                    2, "OK", NewUniqueCandidates: 1),
                new DriverSearchPassStatus("Microsoft Update online", "Failed",
                    0, "C:\\Users\\private\\path", NewUniqueCandidates: 0),
                new DriverSearchPassStatus("Servizio aggiuntivo", "NotRegistered",
                    0, "Not registered")
            ]);

        var text = ScanCoverageText.Create(wua,
        [
            new VendorCoverageRow("NVIDIA", 3, 0, 0, 0),
            new VendorCoverageRow("Killer / Intel", 2, 1, 1, 0)
        ], 113, 1, 0);

        Assert.Contains("Windows Update online: interrogata, 2 restituiti, 1 nuovi", text);
        Assert.Contains("Microsoft Update online: ricerca non riuscita", text);
        Assert.Contains("Servizio aggiuntivo: non registrata, non interrogata", text);
        Assert.Contains("NVIDIA: 3 dispositivi", text);
        Assert.Contains("Windows Update: 1 candidati distinti", text);
        Assert.DoesNotContain(@"PCI\VEN_", text);
        Assert.DoesNotContain(@"C:\Users", text);
        Assert.DoesNotContain("71906b24", text);
    }

    [Fact]
    public void OfflineWuaCacheOnlyAddsAnonymousHistoricalCountsToCopy()
    {
        var report = new DriverSearchReport(
            [],
            [new DriverSearchPassStatus("Windows Update online",
                "Completed", 0, "OK")],
            CachedDriverHints:
            [
                new TheEasyWayForDrivers.Core.Update.WuaCachedDriverHint(
                    "00000000-0000-4000-8000-000000000001",
                    "PRIVATE_NVIDIA_DEVICE_UPDATE")
            ],
            CachedDriverSources:
            [
                new DriverSearchPassStatus(
                    TheEasyWayForDrivers.Core.Update.WuaCachedDriverHintCollector.Source,
                    "Completed", 4, "private WUA path")
            ], CachedDriverHintsOmitted: 2);

        var output = ScanCoverageText.Create(report, [], 1, 0, 0);
        Assert.Contains("Cache Windows locale (storico, SOLO informativa)", output);
        Assert.Contains("1 titoli distinti", output);
        Assert.Contains("2 oltre il limite", output);
        Assert.Contains("NON sono nuove offerte online", output);
        Assert.DoesNotContain("PRIVATE", output);
        Assert.DoesNotContain("00000000-0000-4000", output);
        Assert.DoesNotContain("private WUA path", output);
        Assert.Contains("Windows Update: 0 candidati distinti", output);
    }

    [Fact]
    public void InstalledAndNotInstalledHistoryAreReportedAnonymously()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 0, "")
        ], CachedDriverHints:
        [
            new TheEasyWayForDrivers.Core.Update.WuaCachedDriverHint(
                "00000000-0000-4000-8000-000000000015",
                "PRIVATE_INSTALLED_REALTEK_DRIVER", true),
            new TheEasyWayForDrivers.Core.Update.WuaCachedDriverHint(
                "00000000-0000-4000-8000-000000000016",
                "PRIVATE_NOT_INSTALLED_KILLER_DRIVER", false)
        ], CachedDriverSources:
        [
            new DriverSearchPassStatus("Cache Windows locale · non installati",
                "Completed", 1, ""),
            new DriverSearchPassStatus("Cache Windows locale · già installati",
                "Completed", 1, "")
        ]);
        var summary = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains("2 titoli distinti", summary);
        Assert.Contains("1 contrassegnati come installati", summary);
        Assert.Contains("1 non installati", summary);
        Assert.DoesNotContain("PRIVATE", summary);
        Assert.DoesNotContain("00000000-0000", summary);
        Assert.False(report.IsPartial);
    }

    [Fact]
    public void OfflineInstalledPassFailureKeepsVerifiedUninstalledCacheCounts()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 0, "")
        ], CachedDriverHints:
        [
            new TheEasyWayForDrivers.Core.Update.WuaCachedDriverHint(
                "00000000-0000-4000-8000-000000000017",
                "PRIVATE_UNINSTALLED_NVIDIA_DRIVER", false)
        ], CachedDriverSources:
        [
            new DriverSearchPassStatus("Cache Windows locale · non installati",
                "Completed", 1, ""),
            new DriverSearchPassStatus("Cache Windows locale · già installati",
                "Failed", 0, "PRIVATE_DEVICE_ID")
        ]);
        var summary = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains("1 titoli distinti", summary);
        Assert.Contains("Fonti offline parziali/non verificate", summary);
        Assert.DoesNotContain("PRIVATE", summary);
        Assert.False(report.IsPartial);
        Assert.False(report.AllFailed);
        Assert.True(report.CachedDriverSearchFailed);
    }

    [Fact]
    public void CacheOfflineFailureDoesNotInvalidateCompletedOnlineScan()
    {
        var report = new DriverSearchReport(
            [],
            [new DriverSearchPassStatus("Windows Update online",
                "Completed", 0, "OK")],
            CachedDriverSources:
            [
                new DriverSearchPassStatus(
                    TheEasyWayForDrivers.Core.Update.WuaCachedDriverHintCollector.Source,
                    "Failed", 0, "PRIVATE_USER_PROFILE")
            ]);
        var text = ScanCoverageText.Create(report, [], 0, 0, 0);
        Assert.Contains("ricerca offline non verificata", text);
        Assert.Contains("Windows Update: 0 candidati distinti", text);
        Assert.False(report.IsPartial);
        Assert.True(report.CachedDriverSearchFailed);
        Assert.DoesNotContain("PRIVATE_USER_PROFILE", text);
    }

    [Fact]
    public void MissingScanMustNotClaimZeroAvailableDrivers()
    {
        var text = ScanCoverageText.Create(null, [], 50, 1, 0);
        Assert.Contains("ricerca non verificata", text);
        Assert.Contains("nessuna fonte verificata", text);
        Assert.DoesNotContain("0 candidati distinti", text);
    }

    [Fact]
    public void ReportShowsInformationalSoftwareFailuresWithoutPrivateDetails()
    {
        var report = new DriverSearchReport(
            [],
            [new DriverSearchPassStatus("Windows Update online",
                "Completed", 0, "OK")],
            RelatedSoftwareSources:
            [new DriverSearchPassStatus("Windows Update online · componenti software",
                "Failed", 0, @"C:\\Users\\private\\appdata")]);

        var output = ScanCoverageText.Create(report, [], 10, 0, 0);
        Assert.Contains("Verifica software OEM incompleta", output);
        Assert.Contains("Type=Driver resta indipendente", output);
        Assert.Contains("fonte non verificata", output);
        Assert.DoesNotContain("private", output);
        Assert.False(report.IsPartial);
    }
}
