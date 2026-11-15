using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #452: a first bare VEN+DEV cannot mask two mutually exclusive OEM
/// SUBSYS identities on later rows for the SAME Windows PnP instance.
/// Excluding this instance is safer than matching WUA/INF by WMI row order.
/// All diagnostics remain read-only and private.
/// </summary>
public sealed class PnpLateSubsysConflictTests
{
    private const string NvidiaBase = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaA = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaB = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A21043";
    private const string KillerBase = @"PCI\VEN_8086&DEV_51F1";
    private const string KillerA = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string KillerB = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721043";
    private const string RealtekBase = @"PCI\VEN_10EC&DEV_8125";
    private const string RealtekA = @"PCI\VEN_10EC&DEV_8125&SUBSYS_012310EC";
    private const string RealtekB = @"PCI\VEN_10EC&DEV_8125&SUBSYS_567810EC";
    private const string NvidiaAudioBase =
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string NvidiaAudioA =
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B&SUBSYS_12345678";
    private const string NvidiaAudioB =
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B&SUBSYS_87654321";
    private const string WuaGuid =
        "00000000-0000-4000-8000-000000000452";

    private static DriverInfo Device(string instance, string id) =>
        new(instance, "Windows generic device", "Microsoft", "Unknown",
            "Microsoft", "31.0.0.1", null, "PRIVATE_oem1.inf",
            true, true, 0, [id], []);

    private static DriverUpdateInfo Update(string id) =>
        new(WuaGuid, "Windows driver", null, "Unknown", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    private static DriverSearchReport Report(string id) =>
        new([Update(id)],
            [new DriverSearchPassStatus(
                "Windows Update online", "Completed", 1, "verified")]);

    [Theory]
    [InlineData(NvidiaBase, NvidiaA, NvidiaB)]
    [InlineData(KillerBase, KillerA, KillerB)]
    [InlineData(RealtekBase, RealtekA, RealtekB)]
    [InlineData(NvidiaAudioBase, NvidiaAudioA, NvidiaAudioB)]
    public void FirstBareWithTwoDifferentLateSubsysCannotCertifyHardware(
        string bareId, string oemA, string oemB)
    {
        var bare = Device("PRIVATE_SAME_INSTANCE", bareId);
        var a = Device("private_same_instance", oemA);
        var b = Device("PRIVATE_SAME_INSTANCE", oemB);
        var duplicateBare = Device("private_same_instance", bareId);
        foreach (var rows in new[]
        {
            new[] { bare, a, b },
            new[] { bare, b, a },
            new[] { bare, duplicateBare, a, b },
            new[] { a, bare, b },
            new[] { b, bare, a }
        })
        {
            Assert.Empty(PnpDistinctTrustedDevices.Create(rows));
            var audit = WuaPnpAssociationAudit.Create(
                rows, Report(bareId), true);
            Assert.Equal(1, audit.ConfirmedGuids);
            Assert.Equal(0, audit.ExactHardware);
            Assert.Equal(0, audit.CompatibleOnly);
            Assert.Equal(1, audit.Unattributed);
            Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
            Assert.Contains("identità hardware contraddittorie", audit.Text);
            Assert.DoesNotContain("PRIVATE", audit.Text);
            Assert.DoesNotContain(bareId, audit.Text);
            Assert.False(audit.IsInstallable);
            Assert.Empty(VendorSourceDiscovery.Find(
                rows, [Update(bareId)]));
        }
    }

    [Fact]
    public void AmbiguousBareBaseCannotInflateHomeBenchmarkOrOemInf()
    {
        var bare = Device("PRIVATE_GPU", NvidiaBase);
        var a = Device("private_gpu", NvidiaA);
        var b = Device("PRIVATE_GPU", NvidiaB);
        var rows = new[] { bare, a, b };
        var report = Report(NvidiaBase);

        var home = HomeVendorCoverageSummary.Create(
            rows, report, inventoryComplete: true);
        Assert.All(home, item =>
        {
            Assert.Contains("0 dispositivi PnP", item.DeviceStatus);
            Assert.Contains("0 candidati WUA per ID", item.WindowsStatus);
            Assert.False(item.HasLocalDevice);
            Assert.False(item.IsInstallable);
        });

        var inventory = VendorInventoryCoverage.Create(rows, report.Updates);
        Assert.Empty(inventory.Rows);
        var eight = ReferenceDriverCoverage.Create(rows, report);
        Assert.Equal(8, eight.Count);
        Assert.All(eight, item =>
        {
            Assert.Equal(0, item.Devices);
            Assert.Equal(0, item.WindowsDriverCandidates);
        });

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(
            stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(
                zip.CreateEntry("nvidia/driver.inf").Open(),
                new UTF8Encoding(false));
            writer.Write("[Version]\nSignature=\"$Windows NT$\"\n" +
                "Class=Display\nDriverVer=10/04/2026,32.0.15.7000\n" +
                "[Manufacturer]\n%GPU%=Models,NTamd64.10.0...22000\n" +
                "[Models.NTamd64.10.0...22000]\n" +
                "%GPU%=Install," + NvidiaBase + "\n");
        }
        stream.Position = 0;
        var comparison = LocalOemDriverComparison.Compare(
            stream, rows, "x64", 10, 0, 26100);
        Assert.Empty(comparison.Findings);
        Assert.Empty(comparison.MatchedDeviceOrdinals ?? []);
        Assert.Equal(1, comparison.ExcludedAmbiguousPnpInstances);
        Assert.False(comparison.IsInstallable);

        var summary = ScanCoverageText.Create(
            report, inventory.Rows, rows.Length, 0, 0,
            rows, true, vendorCoverage: inventory);
        Assert.DoesNotContain("PRIVATE", summary);
        Assert.DoesNotContain(NvidiaBase, summary);
        Assert.DoesNotContain(WuaGuid, summary);
    }

    [Theory]
    [InlineData(NvidiaBase, NvidiaA)]
    [InlineData(KillerBase, KillerA)]
    [InlineData(RealtekBase, RealtekA)]
    [InlineData(NvidiaAudioBase, NvidiaAudioA)]
    public void SingleCoherentOemSubsysIsStillTrusted(
        string bareId, string qualified)
    {
        var baseRow = Device("PRIVATE_INSTANCE", bareId);
        var precise = Device("private_instance", qualified);
        var anotherBase = Device("PRIVATE_INSTANCE", bareId);
        foreach (var rows in new[]
        {
            new[] { baseRow, precise },
            new[] { precise, baseRow },
            new[] { baseRow, anotherBase, precise }
        })
        {
            var selected = Assert.Single(
                PnpDistinctTrustedDevices.Create(rows));
            // PCI promotes its real OEM HardwareID, while non-PCI
            // retains the existing conservative first-row behavior.
            Assert.True(selected.HardwareIds.Count > 0);
            Assert.Equal(0, WuaPnpAssociationAudit.Create(
                rows, Report(qualified), true)
                .ExcludedAmbiguousPnpInstances);
        }
    }

    [Fact]
    public void AnUnrelatedVerifiedKillerStillCorrelates()
    {
        var badBare = Device("PRIVATE_GPU", NvidiaBase);
        var badA = Device("private_gpu", NvidiaA);
        var badB = Device("PRIVATE_GPU", NvidiaB);
        var good = Device("PRIVATE_KILLER", KillerA);

        var kept = Assert.Single(PnpDistinctTrustedDevices.Create(
            [badBare, badA, badB, good]));
        Assert.Same(good, kept);
        var audit = WuaPnpAssociationAudit.Create(
            [badBare, badA, badB, good], Report(KillerA), true);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
        Assert.Equal(0, audit.Unattributed);
    }

    [Fact]
    public void CompatibleOnlyAlternateSubsysBlocksPromotionNotWholeInstance()
    {
        var bare = Device("PRIVATE_GPU", NvidiaBase);
        var qualified = Device("private_gpu", NvidiaA);
        var compatibleOnly = Device("PRIVATE_GPU", NvidiaBase) with
        {
            CompatibleIds = [NvidiaB]
        };
        var rows = new[] { bare, qualified, compatibleOnly };
        var chosen = Assert.Single(PnpDistinctTrustedDevices.Create(rows));
        // Existing fail-closed promotion policy refuses an OEM board when
        // a CompatibleId disagrees; a compatible alias alone is NOT proof
        // that the HardwareId of this instance really changed boards.
        // Keep only the real first bare ID, not the conflicting OEM ID.
        Assert.Same(bare, chosen);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            chosen, Update(NvidiaA)));
        var audit = WuaPnpAssociationAudit.Create(
            rows, Report(NvidiaA), true);
        Assert.Equal(0, audit.ExcludedAmbiguousPnpInstances);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(1, audit.Unattributed);
    }
}
