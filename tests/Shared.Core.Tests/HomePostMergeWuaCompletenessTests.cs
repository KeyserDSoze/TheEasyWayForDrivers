using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #467: Home cards and the eight-row/copyable QA report must assess
/// incompleteness AFTER WUA GUID/source merge, not before sanitization
/// of malformed legacy IPC hardware identifiers.
/// </summary>
public sealed class HomePostMergeWuaCompletenessTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string GuidOne = "00000000-0000-4000-8000-000000000467";
    private const string GuidTwo = "00000000-0000-4000-8000-000000000468";

    private static DriverInfo Device(string instance, string name,
        string cls, string hardware) =>
        new(instance, name, "Microsoft", cls, "Microsoft",
            "1.0", null, "oem5.inf", true, true, 0, [hardware], []);

    private static readonly DriverInfo NvidiaDevice =
        Device("PRIVATE_NVIDIA_INSTANCE", "Microsoft Basic Display",
            "Display", Gpu);
    private static readonly DriverInfo KillerDevice =
        Device("PRIVATE_KILLER_INSTANCE", "Network Controller",
            "Net", Killer);
    private static readonly DriverInfo RealtekDevice =
        Device("PRIVATE_AUDIO_INSTANCE", "High Definition Audio Device",
            "MEDIA", Realtek);

    private static DriverUpdateInfo Update(string guid, string? hardware,
        IReadOnlyList<string>? extras = null,
        string source = "Windows Update online",
        bool incomplete = false) =>
        new(guid, "PRIVATE_WUA_TITLE", null, "System",
            "Microsoft", null, null, false, "Microsoft",
            null, hardware, null, SearchSource: source,
            AdditionalHardwareIds: extras,
            AdditionalHardwareIdsIncomplete: incomplete);

    private static DriverSearchPassStatus Pass(
        string source = "Windows Update online", string outcome = "Completed") =>
        new(source, outcome, 1, "PRIVATE_WSUS_ENDPOINT", 1);

    private static DriverSearchReport Report(
        IReadOnlyList<DriverUpdateInfo> offers,
        params DriverSearchPassStatus[] sources) =>
        new(offers, sources.Length > 0 ? sources : [Pass()]);

    private static string HomeText(
        IReadOnlyList<HomeVendorCoverageSummary.Row> rows) =>
        string.Join(" ", rows.Select(x => x.Group + " · " +
            x.DeviceStatus + " · " + x.WindowsStatus));

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\t")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\u202E")]
    public void RejectedWuaPrimaryWarnsHomeEvenWhenRawFlagSaysComplete(
        string malformed)
    {
        var raw = Update(GuidOne, malformed);
        Assert.False(raw.AdditionalHardwareIdsIncomplete);
        var report = Report([raw]);
        var devices = new[] { NvidiaDevice, KillerDevice, RealtekDevice };
        var merged = Assert.Single(
            WuaDiagnosticAssociations.Create(
                WindowsDriverCandidateMerge.ConfirmedForReport(report)));

        Assert.Null(merged.PrimaryHardwareId);
        Assert.True(merged.AdditionalHardwareIdsIncomplete);
        Assert.False(merged.Matches(NvidiaDevice));
        var cards = HomeVendorCoverageSummary.Create(devices, report, true);
        Assert.All(cards, row =>
        {
            Assert.Contains("0 correlazioni WUA osservate", row.WindowsStatus);
            Assert.Contains("ID WUA incompleti", row.WindowsStatus);
            Assert.Contains("assenza non verificabile", row.WindowsStatus);
            Assert.DoesNotContain("0 candidati WUA", row.WindowsStatus);
            Assert.False(row.IsInstallable);
        });

        var audit = WuaPnpAssociationAudit.Create(devices, report, true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(1, audit.IncompleteWuaIds);
        Assert.Equal(0, audit.ExactHardware);
        var matrix = ReferenceDriverCoverage.Create(devices, report);
        Assert.True(matrix[5].WuaAssociationsIncomplete);
        Assert.True(matrix[4].WuaAssociationsIncomplete);
        // The historical eight-row benchmark tracks NVIDIA HD Audio,
        // not Realtek codec devices. Realtek belongs to the separate
        // three-vendor Home cards and the global WUA audit.
        Assert.False(matrix[6].WuaAssociationsIncomplete);
        var copy = ScanCoverageText.Create(report, [], devices.Length, 0, 0,
            devices, inventoryComplete: true);
        Assert.Contains("Candidati WUA con associazioni dispositivo incomplete: 1",
            copy);
        var displayed = HomeText(cards) + copy;
        Assert.DoesNotContain("PRIVATE_", displayed);
        Assert.DoesNotContain(GuidOne, displayed);
        Assert.DoesNotContain(@"PCI\", displayed);
        Assert.DoesNotContain(@"HDAUDIO\", displayed);
        Assert.DoesNotContain("SUBSYS", displayed);
    }

    [Fact]
    public void TwoCompletedPassesKeepValidCorrelationsAndPostMergeWarning()
    {
        var first = Update(GuidOne, Gpu);
        var later = Update(GuidOne.ToUpperInvariant(), Realtek,
            [@"PCI\VEN_10DE&DEV_28A0\PRIVATE_ORDER_PATH"],
            source: "Microsoft Update online");
        var report = Report([first, later],
            Pass(), Pass("Microsoft Update online"));
        var raw = WindowsDriverCandidateMerge.ConfirmedForReport(report);
        Assert.All(raw, x => Assert.False(x.AdditionalHardwareIdsIncomplete));
        var merged = Assert.Single(WuaDiagnosticAssociations.Create(raw));
        Assert.True(merged.AdditionalHardwareIdsIncomplete);
        Assert.True(merged.Matches(NvidiaDevice));
        Assert.True(merged.Matches(RealtekDevice));
        Assert.False(merged.Matches(KillerDevice));

        var cards = HomeVendorCoverageSummary.Create(
            [NvidiaDevice, KillerDevice, RealtekDevice], report, true);
        Assert.Contains("1 candidato WUA per ID", cards[0].WindowsStatus);
        Assert.Contains("1 candidato WUA per ID", cards[2].WindowsStatus);
        Assert.Contains("0 correlazioni WUA osservate", cards[1].WindowsStatus);
        Assert.All(cards, x =>
            Assert.Contains("ID WUA incompleti", x.WindowsStatus));
        Assert.Contains("assenza non verificabile", cards[1].WindowsStatus);

        var audit = WuaPnpAssociationAudit.Create(
            [NvidiaDevice, KillerDevice, RealtekDevice], report, true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(1, audit.IncompleteWuaIds);
        Assert.Equal(1, audit.ExactHardware);
        Assert.False(audit.IsInstallable);
        Assert.DoesNotContain("PRIVATE_", HomeText(cards));
    }

    [Fact]
    public void FailedUnknownAndNonGuidSourcesCannotInventMissingIdWarning()
    {
        var valid = Update(GuidOne, @"PCI\VEN_9999&DEV_0100");
        var invalid = Update(GuidTwo, Gpu + "\n",
            source: "Microsoft Update online");
        var untrusted = Update(GuidOne, Realtek + "\t",
            source: "PRIVATE_OEM_MIRROR", incomplete: true);
        var nonGuid = Update("PRIVATE_NOT_A_WUA_GUID", Killer + "\n");
        var report = Report([valid, invalid, untrusted, nonGuid],
            Pass(), Pass("Microsoft Update online", "NetworkError"));
        var cards = HomeVendorCoverageSummary.Create(
            [NvidiaDevice, KillerDevice, RealtekDevice], report, true);
        Assert.All(cards, row =>
        {
            Assert.Contains("0 candidati WUA per ID", row.WindowsStatus);
            Assert.Contains("fonti Windows parziali", row.WindowsStatus);
            Assert.DoesNotContain("ID WUA incompleti", row.WindowsStatus);
        });
        var audit = WuaPnpAssociationAudit.Create(
            [NvidiaDevice, KillerDevice, RealtekDevice], report, true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(0, audit.IncompleteWuaIds);
        Assert.DoesNotContain("PRIVATE_", HomeText(cards));
    }

    [Fact]
    public void GenuineEmptyWuaEntryIsNotAnIncompleteTransport()
    {
        var report = Report([Update(GuidOne, null)]);
        var card = HomeVendorCoverageSummary.Create(
            [NvidiaDevice], report, true)[0];
        Assert.Contains("0 candidati WUA per ID", card.WindowsStatus);
        Assert.DoesNotContain("ID WUA incompleti", card.WindowsStatus);
        Assert.Equal(1, WuaPnpAssociationAudit.Create(
            [NvidiaDevice], report, true).MissingWuaIds);
    }

    [Fact]
    public void UnverifiedInventoryNeverClaimsAZeroOrDriverAvailability()
    {
        var report = Report([Update(GuidOne, Gpu + "\t")]);
        var rows = HomeVendorCoverageSummary.Create(
            [NvidiaDevice], report, false);
        Assert.All(rows, row =>
        {
            Assert.Contains("Inventario PnP non verificato", row.DeviceStatus);
            Assert.Equal("Correlazione WUA non disponibile", row.WindowsStatus);
            Assert.False(row.IsInstallable);
            Assert.False(row.CanInspectLocalDrivers);
        });
    }
}
