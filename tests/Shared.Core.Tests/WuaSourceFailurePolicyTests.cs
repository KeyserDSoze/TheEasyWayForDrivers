using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaSourceFailurePolicyTests
{
    [Fact]
    public void MicrosoftUpdateNotRegisteredDoesNotTurnCompletedScanPartial()
    {
        var report = new DriverSearchReport(
            [],
            [
                new DriverSearchPassStatus("Windows configurato", "Completed", 0, "ok"),
                new DriverSearchPassStatus("Windows Update online", "Completed", 0, "ok"),
                WindowsDriverSearchPlan.NotRegisteredStatus()
            ]);

        Assert.False(report.IsPartial);
        Assert.False(report.AllFailed);
        Assert.Empty(WuaSourceFailurePolicy.UnavailableSources(report));

        var text = SimpleScanPresentation.Create(report, true, 335, 3, 0,
            WuaSourceFailurePolicy.UnavailableSources(report));
        Assert.DoesNotContain("Alcuni controlli non sono riusciti", text.Warning);
        Assert.Contains("nessun risultato non significa driver aggiornati", text.Warning);
    }

    [Fact]
    public void FailedMicrosoftUpdateRegistrationStillCountsAsUnverified()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows configurato", "Completed", 0, "ok"),
            WindowsDriverSearchPlan.RegistrationFailureStatus()
        ]);

        var failed = WuaSourceFailurePolicy.UnavailableSources(report);
        Assert.Equal(["Microsoft Update online"], failed);
        Assert.True(report.IsPartial);
        var text = SimpleScanPresentation.Create(report, true, 40, 1, 0, failed);
        Assert.Contains("Alcuni controlli driver non sono riusciti", text.Warning);
    }

    [Fact]
    public void FailedPassRemainsReportedButRegisteredAbsenceIsNotError()
    {
        var report = new DriverSearchReport([], [
            new DriverSearchPassStatus("Windows configurato", "Completed", 2, "ok"),
            new DriverSearchPassStatus("Windows Update online", "Failed", 0, "offline"),
            WindowsDriverSearchPlan.NotRegisteredStatus()
        ]);
        Assert.Equal(["Windows Update online"],
            WuaSourceFailurePolicy.UnavailableSources(report));
        Assert.True(report.IsPartial);
    }


    private static readonly string NvidiaId = @"PCI\VEN_10DE&DEV_28A0";
    private static readonly string KillerId =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";

    private static DriverUpdateInfo Offer(int number, string source,
        string hardware = @"PCI\VEN_10DE&DEV_28A0") =>
        new($"00000000-0000-4000-8000-{number:D12}",
            "PRIVATE_COM_DEVICE_TITLE", null, "Display",
            "Microsoft", null, null, false, "Microsoft", null,
            hardware, null, SearchSource: source);

    [Fact]
    public void CompletedWithZeroComRowsAndOneOwnOfferIsUnavailableInHome()
    {
        var report = new DriverSearchReport(
            [Offer(554, "Windows Update online")],
            [new DriverSearchPassStatus(
                "Windows Update online", "Completed", 0,
                "PRIVATE_WSUS_SERVER", AttemptedAtUtc: DateTimeOffset.UtcNow)]);

        Assert.True(report.IsPartial);
        Assert.True(report.AllFailed);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedForReport(report));

        var unavailable = WuaSourceFailurePolicy.UnavailableSources(report);
        Assert.Equal(["Windows Update online"], unavailable);
        var home = SimpleScanPresentation.Create(
            report, inventoryComplete: true, inventoriedDevices: 1,
            problems: 0, oemRecommended: 0, unavailableSteps: unavailable);
        Assert.Contains("non completata", home.Headline);
        Assert.Contains("Alcuni controlli driver non sono riusciti", home.Warning);

        var detail = Assert.Single(WuaPassStatusPresentation.Create(report));
        Assert.Contains("NON verificata", detail);
        Assert.DoesNotContain("ricerca completata", detail);
        Assert.DoesNotContain("PRIVATE_", home.Warning + detail);
        Assert.DoesNotContain(NvidiaId, home.Warning + detail);
    }

    [Fact]
    public void CorruptOneSourceDoesNotHideOtherIndependentlyVerifiedDriver()
    {
        // The bad online source reports one COM row but exports two
        // GUIDs. Only the healthy configured source may attest Killer.
        const string configured = "Windows configurato";
        const string online = "Windows Update online";
        var valid = Offer(554, configured, KillerId);
        var report = new DriverSearchReport(
            [Offer(555, online), Offer(556, online), valid],
            [
                new DriverSearchPassStatus(online, "Completed", 1,
                    "PRIVATE_ONLINE_SERVER"),
                new DriverSearchPassStatus(configured, "Completed", 1,
                    "PRIVATE_CONFIGURED_WSUS"),
                WindowsDriverSearchPlan.NotRegisteredStatus()
            ]);

        Assert.True(report.IsPartial);
        Assert.False(report.AllFailed);
        var confirmed = Assert.Single(
            WindowsDriverCandidateMerge.ConfirmedDistinctForReport(report));
        Assert.Equal(valid.Id, confirmed.Id);
        var unavailable = WuaSourceFailurePolicy.UnavailableSources(report);
        Assert.Equal([online], unavailable);
        Assert.DoesNotContain(configured, unavailable);
        Assert.DoesNotContain("Microsoft Update online", unavailable);

        var home = SimpleScanPresentation.Create(
            report, true, 3, 0, 0, unavailable);
        Assert.Contains("Risultati parziali", home.Headline);
        Assert.Contains("1 possibile aggiornamento", home.Headline);
        Assert.Contains("Alcuni controlli driver non sono riusciti", home.Warning);
        var joined = home.Warning + " " + string.Join(" ", unavailable);
        Assert.DoesNotContain("PRIVATE_", joined);
        Assert.DoesNotContain(valid.Id, joined);
        Assert.DoesNotContain(KillerId, joined);
    }

    [Fact]
    public void UnknownIpcSourceNameIsNeverEchoedIntoUnavailableList()
    {
        const string unknown = @"C:\Users\PRIVATE_OWNER\WSUS\server";
        var report = new DriverSearchReport(
            [Offer(554, unknown)],
            [
                new DriverSearchPassStatus(unknown, "Completed", 1,
                    "PRIVATE_OWNERS_SERIAL"),
                new DriverSearchPassStatus("Windows configurato",
                    "Completed", 0, "ok")
            ]);
        var unavailable = WuaSourceFailurePolicy.UnavailableSources(report);

        Assert.Equal([WuaSourceFailurePolicy.UnknownSourceLabel], unavailable);
        Assert.DoesNotContain("PRIVATE_", string.Join(" ", unavailable));
        Assert.DoesNotContain(@"C:\", string.Join(" ", unavailable));
    }

    [Fact]
    public void NullReportHasNoPerSourceFailures() =>
        Assert.Empty(WuaSourceFailurePolicy.UnavailableSources(null));

    [Fact]
    public void SoftwareOnlyFailureIsReportedSeparatelyFromDriverSourceFailures()
    {
        var report = new DriverSearchReport(
            [],
            [new DriverSearchPassStatus("Windows configurato",
                "Completed", 0, "OK")],
            RelatedSoftwareSources:
            [new DriverSearchPassStatus("Windows Update online · componenti software",
                "Failed", 0, "Offline")]);

        Assert.Empty(WuaSourceFailurePolicy.UnavailableSources(report));
        Assert.False(report.IsPartial);
        Assert.True(report.SoftwareSearchIsPartial);
    }
}
