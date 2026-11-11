using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
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
                    false, null, null, @"PCI\VEN_10DE&DEV_28A0", null,
                    SearchSource: "Windows Update online")
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
        Assert.Contains("Fonte Windows non riconosciuta: non registrata, non interrogata", text);
        Assert.DoesNotContain("Servizio aggiuntivo", text);
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
        Assert.Contains("ricerche offline non verificate", text);
        Assert.Contains("Windows Update: 0 candidati distinti", text);
        Assert.False(report.IsPartial);
        Assert.True(report.CachedDriverSearchFailed);
        Assert.DoesNotContain("PRIVATE_USER_PROFILE", text);
    }


    [Fact]
    public void PolicyNetworkAndUtcTimestampArePrivacySafeInReport()
    {
        var time = new DateTimeOffset(2026, 11, 9, 9, 30, 0,
            TimeSpan.FromHours(1));
        var report = new DriverSearchReport([],
        [
            new DriverSearchPassStatus("Windows Update online",
                "PolicyRestricted", 0, "PRIVATE_PATH", AttemptedAtUtc: time),
            new DriverSearchPassStatus("Microsoft Update online",
                "NetworkError", 0, "PRIVATE_PROXY", AttemptedAtUtc: time),
            WindowsDriverSearchPlan.NotRegisteredStatus()
        ]);
        var copy = ScanCoverageText.Create(report, [], 10, 0, 0);
        Assert.Contains("Windows Update online: ricerca bloccata dai criteri Windows", copy);
        Assert.Contains("Microsoft Update online: errore di connessione/proxy/server", copy);
        Assert.Equal(2, copy.Split("tentata 2026-11-09 08:30:00 UTC").Length - 1);
        Assert.Contains("non registrata, non interrogata", copy);
        Assert.DoesNotContain("PRIVATE", copy);
        Assert.True(report.IsPartial);
        Assert.True(report.AllFailed);
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

    [Fact]
    public void ExportReportsUnknownWindowsProblemCodesOnlyAsAnonymousCounts()
    {
        var device = new DriverInfo(
            @"PCI\\VEN_10DE&DEV_28A0\\PRIVATE_ORDER_SERIAL",
            "PRIVATE PC GPU", "NVIDIA", "Display", "NVIDIA",
            "32.0", null, "nv.inf", true, true, 0,
            [@"PCI\\VEN_10DE&DEV_28A0"], [],
            PnpProblemCodeVerified: false);
        var output = ScanCoverageText.Create(null, [], 1, 0, 0,
            [device], inventoryComplete: true);
        Assert.Contains("Codici problema Windows PnP non verificati: 1", output);
        Assert.DoesNotContain("PRIVATE", output);
        Assert.DoesNotContain("VEN_10DE", output);
        Assert.DoesNotContain("SERIAL", output);
        Assert.Contains("non significa", output);
    }


    [Theory]
    [InlineData("PRIVATE_PC_HOSTNAME")]
    [InlineData("PRIVATE_WSUS_PROXY")]
    [InlineData("PRIVATE_ORDER_1234")]
    [InlineData("PRIVATE_TOKEN_0123456789")]
    public void ShortUntrustedWuaSourceNamesNeverLeaveAnonymousCopy(
        string privateSource)
    {
        var report = new DriverSearchReport([],
        [
            new DriverSearchPassStatus(privateSource,
                "Failed", 0, "PRIVATE_ERROR_DETAIL"),
            new DriverSearchPassStatus(privateSource,
                "PolicyRestricted", 4, "PRIVATE_WSUS_PATH"),
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 2, "PRIVATE_COM_DETAIL", 1)
        ]);
        var result = ScanCoverageText.Create(report, [], 3, 1, 0);
        Assert.Equal(2, result.Split("Fonte Windows non riconosciuta").Length - 1);
        Assert.Contains("ricerca non riuscita", result);
        Assert.Contains("ricerca bloccata dai criteri Windows", result);
        Assert.Contains("Windows Update online: interrogata, 2 restituiti, 1 nuovi",
            result);
        Assert.DoesNotContain(privateSource, result);
        Assert.DoesNotContain("PRIVATE", result);
    }

    [Fact]
    public void SoftwareSourceUsesIndependentOfficialSourceWhitelist()
    {
        var report = new DriverSearchReport([],
            [new DriverSearchPassStatus("Windows configurato", "Completed",
                1, "PRIVATE_WUA")],
            RelatedSoftwareSources:
            [
                new DriverSearchPassStatus("PRIVATE_PC_SOFTWARE",
                    "Completed", 7, "PRIVATE_PKG"),
                new DriverSearchPassStatus(
                    "Windows Update online · componenti software",
                    "Completed", 2, "PRIVATE_NET")
            ]);
        var result = ScanCoverageText.Create(report, [], 6, 0, 0);
        Assert.Contains("Fonte software Windows non riconosciuta: interrogata",
            result);
        Assert.Contains(
            "Windows Update online · componenti software: interrogata",
            result);
        Assert.DoesNotContain("PRIVATE", result);
        Assert.DoesNotContain("PRIVATE_PC_SOFTWARE", result);
    }

    [Fact]
    public void UnknownVendorLabelsAreNeverEchoedEvenIfTheyLookHarmless()
    {
        var report = new DriverSearchReport([],
            [new DriverSearchPassStatus("Windows Update online",
                "Completed", 0, "PRIVATE")]);
        var rows = new[]
        {
            new VendorCoverageRow("PRIVATE_ORDER_SERIAL", 3, 0, 0, 1),
            new VendorCoverageRow("nViDiA", 2, 1, 1, 0),
            new VendorCoverageRow("Killer / Intel", 1, 0, 0, 0),
            new VendorCoverageRow("PRIVATE_MACHINE", 5, 0, 0, 0),
            new VendorCoverageRow("Qualcomm / Atheros", 2, 0, 0, 0)
        };
        var result = ScanCoverageText.Create(report, rows, 13, 0, 0);
        Assert.Contains("altro produttore: 3 dispositivi", result);
        Assert.Contains("altro produttore: 5 dispositivi", result);
        Assert.Contains("NVIDIA: 2 dispositivi", result);
        Assert.Contains("Killer / Intel: 1 dispositivi", result);
        Assert.Contains("Qualcomm / Atheros: 2 dispositivi", result);
        Assert.DoesNotContain("PRIVATE", result);
        Assert.DoesNotContain("nViDiA", result);
    }

    [Fact]
    public void OfficialAdvancedAndMicrosoftWuaSourcesRetainTheirStatuses()
    {
        var report = new DriverSearchReport([],
        [
            new DriverSearchPassStatus("Windows configurato · avanzato",
                "Partial", 9, "PRIVATE"),
            new DriverSearchPassStatus("Microsoft Update online · avanzato",
                "NetworkError", 0, "PRIVATE"),
            WindowsDriverSearchPlan.NotRegisteredStatus()
        ]);
        var result = ScanCoverageText.Create(report, [], 2, 0, 0);
        Assert.Contains(
            "Windows configurato · avanzato: ricerca con avvisi/errori",
            result);
        Assert.Contains(
            "Microsoft Update online · avanzato: errore di connessione",
            result);
        Assert.Contains(
            "Microsoft Update online: non registrata, non interrogata",
            result);
        Assert.DoesNotContain("PRIVATE", result);
    }

}
