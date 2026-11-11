using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// The anonymous 8-vs-2 QA export must report the same trusted, merged WUA
/// GUID totals as its own PnP audit, never count raw IPC rows as new drivers.
/// </summary>
public sealed class ScanCoverageVerifiedGuidTotalsTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0";
    private const string Audio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string GuidOne = "00000000-0000-4000-8000-000000000351";
    private const string GuidTwo = "00000000-0000-4000-8000-000000000352";

    private static DriverInfo Device(string serial, string id,
        string deviceClass) =>
        new(serial, "PRIVATE DEVICE TITLE", "Microsoft",
            deviceClass, "Microsoft", "1.0", null, "PRIVATE_PACKAGE.inf",
            true, true, 0, [id], []);

    private static DriverUpdateInfo Wua(string guid, string id,
        string source = "Windows Update online", bool incomplete = false) =>
        new(guid, "PRIVATE DRIVER TITLE", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: source,
            AdditionalHardwareIdsIncomplete: incomplete);

    private static DriverSearchReport Report(
        IReadOnlyList<DriverUpdateInfo> updates,
        bool completed = true, bool partial = false) =>
        new(updates, partial
            ? [new DriverSearchPassStatus(
                   "Windows Update online", "Completed", updates.Count,
                   "PRIVATE_PROXY"),
               new DriverSearchPassStatus(
                   "Windows Update online · avanzato", "Completed",
                   updates.Count, "PRIVATE_PROXY"),
               new DriverSearchPassStatus(
                   "Microsoft Update online", "NetworkError", 0,
                   "PRIVATE_CREDENTIALS")]
            : completed
                ? [new DriverSearchPassStatus(
                       "Windows Update online", "Completed", updates.Count,
                       "PRIVATE_PROXY"),
                   new DriverSearchPassStatus(
                       "Windows Update online · avanzato", "Completed",
                       updates.Count, "PRIVATE_PROXY"),
                   new DriverSearchPassStatus(
                       "Windows configurato", "Completed", updates.Count,
                       "PRIVATE_PROXY"),
                   new DriverSearchPassStatus(
                       "Microsoft Update online", "Completed", updates.Count,
                       "PRIVATE_PROXY")]
                : [new DriverSearchPassStatus(
                       "Windows Update online", "Failed", 0, "PRIVATE_PROXY")]);

    private static string Copy(
        DriverSearchReport? report,
        IReadOnlyList<DriverInfo>? devices = null,
        bool inventoryComplete = false) =>
        ScanCoverageText.Create(report, [], devices?.Count ?? 0, 0, 0,
            devices, inventoryComplete);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuplicateOfficialGuidAndUntrustedCollisionProduceOneOffer(
        bool externalFirst)
    {
        var gpu = Device("PRIVATE_GPU_INSTANCE", Gpu, "Display");
        var audio = Device("PRIVATE_AUDIO_INSTANCE", Audio, "MEDIA");
        var ordinary = Wua(GuidOne, Gpu, "Windows configurato");
        var anotherWuaPass = Wua(GuidOne.ToUpperInvariant(), Audio,
            "Microsoft Update online", incomplete: true);
        var externalCollision = Wua(GuidOne, Killer,
            "PRIVATE OEM MIRROR", incomplete: true);
        var otherExternal = Wua(GuidTwo, Killer,
            "PRIVATE OEM MIRROR", incomplete: true);
        var invalid = Wua("PRIVATE_NOT_GUID", Gpu, incomplete: true);
        var list = externalFirst
            ? new[] { externalCollision, ordinary, anotherWuaPass,
                otherExternal, invalid }
            : [ordinary, anotherWuaPass, externalCollision,
                otherExternal, invalid];
        var report = Report(list);

        var audit = WuaPnpAssociationAudit.Create([gpu, audio],
            report, inventoryComplete: true);
        var copy = Copy(report, [gpu, audio], true);

        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(1, audit.IncompleteWuaIds);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Contains("Windows Update: 1 candidati distinti", copy);
        Assert.Contains(
            "Candidati WUA con associazioni dispositivo incomplete: 1.",
            copy);
        Assert.Contains("per GUID Type=Driver): 1 offerte", copy);
        Assert.DoesNotContain("Windows Update: 5 candidati", copy);
        Assert.DoesNotContain("dispositivo incomplete: 4", copy);
        Assert.DoesNotContain("PRIVATE", copy);
        Assert.DoesNotContain(GuidOne, copy);
        Assert.DoesNotContain(GuidTwo, copy);
        Assert.DoesNotContain(Gpu, copy);
        Assert.DoesNotContain(Audio, copy);
        Assert.DoesNotContain(Killer, copy);
        Assert.False(audit.IsInstallable);
    }

    [Fact]
    public void UntrustedAndNonGuidOnlyNeverClaimDriverOfferOrIncompleteIds()
    {
        var gpu = Device("PRIVATE_GPU", Gpu, "Display");
        var report = Report(
        [
            Wua(GuidOne, Gpu, "PRIVATE_MANUAL_CATALOG", incomplete: true),
            Wua("NON_GUID", Gpu, incomplete: true),
            Wua(GuidTwo, Killer, "Unverified external site")
        ]);
        var copy = Copy(report, [gpu], true);
        Assert.Contains("Windows Update: 0 candidati distinti", copy);
        Assert.Contains("per GUID Type=Driver): 0 offerte", copy);
        Assert.DoesNotContain("Candidati WUA con associazioni dispositivo incomplete", copy);
        Assert.DoesNotContain("PRIVATE", copy);
        Assert.DoesNotContain("NON_GUID", copy);
        Assert.DoesNotContain(GuidOne, copy);
        Assert.DoesNotContain(Killer, copy);
    }

    [Fact]
    public void CompletelyFailedSearchCannotReportStaleGuidsOrIncompleteIds()
    {
        var gpu = Device("PRIVATE_GPU", Gpu, "Display");
        var stale = Wua(GuidOne, Gpu, incomplete: true);
        var report = Report([stale], completed: false);
        var copy = Copy(report, [gpu], true);

        Assert.Contains("Windows Update: ricerca non verificata", copy);
        Assert.Contains("Diagnostica WUA→PnP: ricerca Type=Driver non verificata",
            copy);
        Assert.DoesNotContain("Windows Update: 0 candidati", copy);
        Assert.DoesNotContain("Windows Update: 1 candidati", copy);
        Assert.DoesNotContain("Candidati WUA con associazioni dispositivo incomplete", copy);
        Assert.DoesNotContain("PRIVATE", copy);
    }

    [Fact]
    public void CompletedPartialAndAdvancedOnlyRemainVisibleNotInstallable()
    {
        var killer = Device("PRIVATE_KILLER", Killer, "Net");
        var advanced = Wua(GuidOne, Killer,
            "Windows Update online · avanzato", incomplete: true) with
        {
            IsAdvancedCandidate = true
        };
        var other = Wua(GuidTwo, Audio,
            "Untrusted supplier", incomplete: true);
        var report = Report([advanced, other], partial: true);
        var copy = Copy(report, [killer], true);

        Assert.True(report.IsPartial);
        Assert.False(QuickUpdatePolicy.IsEligible(advanced));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds([advanced]));
        Assert.Contains("Windows Update: 1 candidati distinti", copy);
        Assert.Contains(
            "Candidati WUA con associazioni dispositivo incomplete: 1.",
            copy);
        Assert.Contains("ricerca parziale", copy);
        Assert.Contains("risultati parziali", copy);
        Assert.Contains("NVIDIA · GPU", copy);
        Assert.DoesNotContain("PRIVATE", copy);
    }

    [Fact]
    public void DuplicatedGuidWithUntrustedIncompleteFlagCannotInventWarning()
    {
        var realtek = Device("PRIVATE_AUDIO", Audio, "MEDIA");
        var valid = Wua(GuidOne, Audio);
        var spoofed = Wua(GuidOne, Audio, "External OEM",
            incomplete: true);
        var copy = Copy(Report([spoofed, valid]), [realtek], true);

        Assert.Contains("Windows Update: 1 candidati distinti", copy);
        Assert.DoesNotContain(
            "Candidati WUA con associazioni dispositivo incomplete", copy);
        Assert.Contains("0 con elenco ID WUA incompleto", copy);
        Assert.DoesNotContain("PRIVATE", copy);
    }

    [Fact]
    public void MissingPnpInventoryStillReportsVerifiedGuidCountOnly()
    {
        var report = Report([
            Wua(GuidOne, Gpu),
            Wua(GuidOne, Audio, "Microsoft Update online"),
            Wua(GuidTwo, Audio, "OEM mirror", incomplete: true)
        ]);
        var copy = Copy(report);
        Assert.Contains("Windows Update: 1 candidati distinti", copy);
        Assert.Contains("inventario PnP non verificato", copy);
        Assert.DoesNotContain(
            "Candidati WUA con associazioni dispositivo incomplete", copy);
        Assert.DoesNotContain("PRIVATE", copy);
    }

    [Fact]
    public void OverBudgetIpcSnapshotDoesNotClaimRawOffersOrZeroDrivers()
    {
        var repeated = Wua(GuidOne, Gpu, incomplete: true);
        var unbounded = Enumerable.Repeat(repeated, 4001).ToArray();
        var report = Report(unbounded);
        var copy = Copy(report);

        Assert.Contains(
            "Windows Update: conteggio candidati non valutabile (limite diagnostico)",
            copy);
        Assert.Contains("Diagnostica WUA→PnP: input oltre il limite", copy);
        Assert.DoesNotContain("Windows Update: 4001 candidati", copy);
        Assert.DoesNotContain("Windows Update: 0 candidati", copy);
        Assert.DoesNotContain(
            "Candidati WUA con associazioni dispositivo incomplete", copy);
        Assert.DoesNotContain(GuidOne, copy);
        Assert.DoesNotContain("PRIVATE", copy);
    }

    [Fact]
    public void NormalVerifiedResultRetainsLegacyHeadingAndSoftwareSeparation()
    {
        var gpu = Device("PRIVATE_GPU", Gpu, "Display");
        var driver = Wua(GuidOne, Gpu);
        var report = new DriverSearchReport(
            [driver],
            [new DriverSearchPassStatus(
                "Windows Update online", "Completed", 1, "PRIVATE")],
            RelatedSoftware:
            [
                new WuaDriverRelatedSoftware(GuidTwo,
                    "Killer Networking Software",
                    "Windows Update online · componenti software")
            ],
            RelatedSoftwareSources:
            [
                new DriverSearchPassStatus(
                    "Windows Update online · componenti software",
                    "Completed", 1, "PRIVATE")
            ]);
        var copy = Copy(report, [gpu], true);

        Assert.Contains("Windows Update: 1 candidati distinti", copy);
        Assert.Contains("NVIDIA · GPU: 1 dispositivi classificati; 1 candidati WUA Type=Driver", copy);
        Assert.Contains("Killer · Networking Software: 1 titoli WUA Type=Software",
            copy);
        Assert.Contains("Componenti software produttore da verificare", copy);
        Assert.DoesNotContain("Killer Networking Software", copy);
        Assert.DoesNotContain("PRIVATE", copy);
    }
}
