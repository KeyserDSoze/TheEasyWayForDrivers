using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #453: generic-first duplicate WMI rows must not hide contradictory
/// concrete HardwareIds of the same recognized vendor; a specific
/// CompatibleId on the generic row can otherwise create a false WUA
/// association without any trustworthy primary PnP identity.
/// </summary>
public sealed class PnpGenericFirstModelConsensusTests
{
    private const string NvidiaA = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaB = @"PCI\VEN_10DE&DEV_1F15";
    private const string NvidiaOemA =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaOemB =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A21043";
    private const string KillerA = @"PCI\VEN_8086&DEV_51F1";
    private const string KillerB = @"PCI\VEN_8086&DEV_2725";
    private const string RealtekA = @"PCI\VEN_10EC&DEV_8125";
    private const string RealtekB = @"PCI\VEN_10EC&DEV_8168";
    private const string NvidiaAudioA =
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string NvidiaAudioB =
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_0083";
    private const string GuidId =
        "00000000-0000-4000-8000-000000000453";

    private static DriverInfo Device(string instance, string hardware) =>
        new(instance, "Windows generic node", "Microsoft", "Unknown",
            "Microsoft", "1.0.0.0", null, "PRIVATE_oem.inf",
            true, true, 0, [hardware], []);

    private static DriverUpdateInfo Update(string id) =>
        new(GuidId, "Windows Type Driver", null, "Unknown",
            "Microsoft", null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    private static DriverSearchReport Report(string id) =>
        new([Update(id)], [new DriverSearchPassStatus(
            "Windows Update online", "Completed", 1, "verified")]);

    [Theory]
    [InlineData(@"PCI\VEN_10DE&CC_0300", NvidiaA, NvidiaB)]
    [InlineData(@"PCI\CC_0300", NvidiaA, NvidiaB)]
    [InlineData(@"PCI\VEN_8086&CC_0280", KillerA, KillerB)]
    [InlineData(@"PCI\VEN_10EC&CC_0200", RealtekA, RealtekB)]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE", NvidiaAudioA, NvidiaAudioB)]
    public void GenericFirstAndDifferentRealModelsCannotCorrelateWua(
        string genericId, string firstModel, string secondModel)
    {
        var weak = Device("PRIVATE_SAME_DEVICE", genericId) with
        {
            CompatibleIds = [firstModel]
        };
        var a = Device("private_same_device", firstModel);
        var b = Device("PRIVATE_SAME_DEVICE", secondModel);
        foreach (var rows in new[]
        {
            new[] { weak, a, b },
            new[] { weak, b, a },
            new[] { a, weak, b },
            new[] { b, weak, a }
        })
        {
            Assert.Empty(PnpDistinctTrustedDevices.Create(rows));
            var audit = WuaPnpAssociationAudit.Create(
                rows, Report(firstModel), true);
            Assert.Equal(1, audit.ConfirmedGuids);
            Assert.Equal(0, audit.ExactHardware);
            Assert.Equal(0, audit.CompatibleOnly);
            Assert.Equal(1, audit.Unattributed);
            Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
            Assert.False(audit.IsInstallable);
            Assert.Contains("identità hardware contraddittorie", audit.Text);
            Assert.DoesNotContain("PRIVATE", audit.Text);
            Assert.DoesNotContain(firstModel, audit.Text);
            Assert.DoesNotContain(GuidId, audit.Text);
            Assert.Empty(VendorSourceDiscovery.Find(rows, [Update(firstModel)]));
        }
    }

    [Theory]
    [InlineData(NvidiaA, NvidiaOemA, NvidiaOemB)]
    [InlineData(KillerA,
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56",
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721043")]
    public void GenericFirstAndDifferentLateOemSubsysExcludeInstance(
        string bare, string qualifiedA, string qualifiedB)
    {
        var weak = Device("PRIVATE_SAME_DEVICE", @"PCI\CC_0300");
        var a = Device("private_same_device", bare);
        var b = Device("PRIVATE_SAME_DEVICE", qualifiedA);
        var c = Device("private_same_device", qualifiedB);
        foreach (var rows in new[]
        {
            new[] { weak, a, b, c },
            new[] { weak, c, b, a }
        })
        {
            Assert.Empty(PnpDistinctTrustedDevices.Create(rows));
            Assert.Equal(1, WuaPnpAssociationAudit.Create(
                rows, Report(qualifiedA), true)
                .ExcludedAmbiguousPnpInstances);
        }
    }

    [Fact]
    public void FalseCompatibleMatchCannotInflateHomeBenchmarkOrInf()
    {
        var weak = Device("PRIVATE_GPU", @"PCI\VEN_10DE&CC_0300")
            with { CompatibleIds = [NvidiaA] };
        var a = Device("private_gpu", NvidiaA);
        var b = Device("PRIVATE_GPU", NvidiaB);
        var rows = new[] { weak, a, b };
        var report = Report(NvidiaA);

        // Without the consensus gate, the generic first row is kept
        // and gives this ambiguous device a Compatible ID WUA match.
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            weak, Update(NvidiaA)));
        var home = HomeVendorCoverageSummary.Create(
            rows, report, inventoryComplete: true);
        Assert.All(home, item =>
        {
            Assert.Contains("0 dispositivi PnP", item.DeviceStatus);
            Assert.Contains("0 candidati WUA per ID", item.WindowsStatus);
            Assert.False(item.HasLocalDevice);
            Assert.False(item.IsInstallable);
        });
        var matrix = VendorInventoryCoverage.Create(rows, report.Updates);
        Assert.Empty(matrix.Rows);
        var eight = ReferenceDriverCoverage.Create(rows, report);
        Assert.Equal(8, eight.Count);
        Assert.All(eight, item =>
        {
            Assert.Equal(0, item.Devices);
            Assert.Equal(0, item.WindowsDriverCandidates);
        });

        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(
            archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(
                zip.CreateEntry("display/driver.inf").Open(),
                new UTF8Encoding(false));
            writer.Write("[Version]\nSignature=\"$Windows NT$\"\n" +
                "DriverVer=10/04/2026,32.0.15.7000\n" +
                "[Manufacturer]\n%GPU%=Models,NTamd64.10.0...22000\n" +
                "[Models.NTamd64.10.0...22000]\n" +
                "%GPU%=Install," + NvidiaA + "\n");
        }
        archive.Position = 0;
        var oem = LocalOemDriverComparison.Compare(
            archive, rows, "x64", 10, 0, 26100);
        Assert.Empty(oem.Findings);
        Assert.Empty(oem.MatchedDeviceOrdinals ?? []);
        Assert.Equal(1, oem.ExcludedAmbiguousPnpInstances);
        Assert.False(oem.IsInstallable);

        var diagnostic = ScanCoverageText.Create(
            report, matrix.Rows, rows.Length, 0, 0,
            rows, true, vendorCoverage: matrix);
        Assert.DoesNotContain("PRIVATE", diagnostic);
        Assert.DoesNotContain(NvidiaA, diagnostic);
        Assert.DoesNotContain(GuidId, diagnostic);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&CC_0300", NvidiaA)]
    [InlineData(@"PCI\VEN_8086&CC_0280", KillerA)]
    [InlineData(@"PCI\VEN_10EC&CC_0200", RealtekA)]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE", NvidiaAudioA)]
    public void OneConcreteModelStillRecoversFromGenericFirst(
        string genericId, string realId)
    {
        var generic = Device("PRIVATE_SAME_DEVICE", genericId);
        var complete = Device("private_same_device", realId);
        var repeated = Device("PRIVATE_SAME_DEVICE", realId);
        var selected = Assert.Single(PnpDistinctTrustedDevices.Create(
            [generic, complete, repeated]));
        Assert.Same(complete, selected);
        var audit = WuaPnpAssociationAudit.Create(
            [generic, complete, repeated], Report(realId), true);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(0, audit.ExcludedAmbiguousPnpInstances);
    }

    [Fact]
    public void DistinctHealthyDeviceStillCorrelatesWhenGpuIsAmbiguous()
    {
        var generic = Device("PRIVATE_GPU", @"PCI\VEN_10DE&CC_0300");
        var a = Device("private_gpu", NvidiaA);
        var b = Device("PRIVATE_GPU", NvidiaB);
        var killer = Device("PRIVATE_KILLER", KillerA);
        var devices = new[] { generic, a, b, killer };

        Assert.Same(killer, Assert.Single(
            PnpDistinctTrustedDevices.Create(devices)));
        var audit = WuaPnpAssociationAudit.Create(
            devices, Report(KillerA), true);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
        Assert.Equal(0, audit.Unattributed);
    }

    [Fact]
    public void CompatibleOnlyDifferentModelNeverClaimsHardwareConflict()
    {
        var primary = Device("PRIVATE_GPU", NvidiaA);
        var secondary = Device("private_gpu", NvidiaA) with
        {
            CompatibleIds = [NvidiaB]
        };
        Assert.Same(primary, Assert.Single(
            PnpDistinctTrustedDevices.Create([primary, secondary])));
        Assert.Equal(0, WuaPnpAssociationAudit.Create(
            [primary, secondary], Report(NvidiaA), true)
            .ExcludedAmbiguousPnpInstances);
    }
}
