using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #450: a duplicated Windows PnP instance with two distinct concrete
/// models of the SAME recognized vendor is not safe to match based on
/// whichever WMI/SignedDriver row happened to be returned first.
/// </summary>
public sealed class PnpSameVendorModelConflictTests
{
    private const string NvidiaA = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaB = @"PCI\VEN_10DE&DEV_1F15";
    private const string NvidiaOemA = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaOemB = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A21043";
    private const string KillerA = @"PCI\VEN_8086&DEV_51F1";
    private const string KillerB = @"PCI\VEN_8086&DEV_2725";
    private const string RealtekA = @"PCI\VEN_10EC&DEV_8125";
    private const string RealtekB = @"PCI\VEN_10EC&DEV_8168";
    private const string NvidiaAudioA = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string NvidiaAudioB = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_0083";
    private const string WuaGuid = "00000000-0000-4000-8000-000000000450";

    private static DriverInfo Device(string instance, string id,
        string name = "Generic Windows device") =>
        new(instance, name, "Microsoft", "Unknown", "Microsoft",
            "31.0.0.1", null, "PRIVATE_oem42.inf",
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
    [InlineData(NvidiaA, NvidiaB)]
    [InlineData(KillerA, KillerB)]
    [InlineData(RealtekA, RealtekB)]
    [InlineData(NvidiaAudioA, NvidiaAudioB)]
    public void SameVendorDifferentConcreteHardwareModelsAreNotOneDevice(
        string modelA, string modelB)
    {
        var first = Device("PRIVATE_SAME_INSTANCE", modelA);
        var second = Device("private_same_instance", modelB);
        Assert.Equal(PnpKnownVendorIdentity.FromId(modelA),
            PnpKnownVendorIdentity.FromId(modelB));
        Assert.NotNull(PnpKnownVendorIdentity.FromId(modelA));

        foreach (var devices in new[]
        {
            new[] { first, second },
            new[] { second, first }
        })
        {
            Assert.Empty(PnpDistinctTrustedDevices.Create(devices));
            Assert.Empty(VendorSourceDiscovery.Find(devices, [Update(modelA)]));
            var audit = WuaPnpAssociationAudit.Create(
                devices, Report(modelA), true);
            Assert.Equal(1, audit.ConfirmedGuids);
            Assert.Equal(0, audit.ExactHardware);
            Assert.Equal(1, audit.Unattributed);
            Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
            Assert.False(audit.IsInstallable);
            Assert.DoesNotContain(modelA, audit.Text);
            Assert.DoesNotContain(modelB, audit.Text);
            Assert.DoesNotContain("PRIVATE", audit.Text);
        }
    }

    [Fact]
    public void AlreadyQualifiedFirstSubsysCannotChooseOneOfTwoOemBoards()
    {
        var laptop = Device("PRIVATE_GPU", NvidiaOemA);
        var different = Device("private_gpu", NvidiaOemB);
        foreach (var devices in new[]
        {
            new[] { laptop, different },
            new[] { different, laptop }
        })
        {
            Assert.Empty(PnpDistinctTrustedDevices.Create(devices));
            var audit = WuaPnpAssociationAudit.Create(
                devices, Report(NvidiaOemA), true);
            Assert.Equal(0, audit.ExactHardware);
            Assert.Equal(1, audit.Unattributed);
            Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
        }
    }

    [Fact]
    public void WrongGpuCannotCreateHomeOfferOrOemInfMatch()
    {
        var first = Device("PRIVATE_GPU", NvidiaA);
        var second = Device("private_gpu", NvidiaB);
        var devices = new[] { first, second };
        var report = Report(NvidiaA);
        var home = HomeVendorCoverageSummary.Create(
            devices, report, inventoryComplete: true);
        Assert.All(home, row =>
        {
            Assert.Contains("0 dispositivi PnP", row.DeviceStatus);
            Assert.Contains("0 candidati WUA per ID", row.WindowsStatus);
            Assert.False(row.HasLocalDevice);
            Assert.False(row.IsInstallable);
        });
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            devices, "NVIDIA", inventoryComplete: true));

        var coverage = VendorInventoryCoverage.Create(devices, report.Updates);
        Assert.Empty(coverage.Rows);
        var reference = ReferenceDriverCoverage.Create(devices, report);
        Assert.Equal(8, reference.Count);
        Assert.All(reference, row =>
        {
            Assert.Equal(0, row.Devices);
            Assert.Equal(0, row.WindowsDriverCandidates);
        });

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(
            stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(
                zip.CreateEntry("display/oem.inf").Open(),
                new UTF8Encoding(false));
            writer.Write("[Version]\nSignature=\"$Windows NT$\"\n" +
                "Class=Display\nDriverVer=10/04/2026,32.0.15.7000\n" +
                "[Manufacturer]\n%GPU%=Models,NTamd64.10.0...22000\n" +
                "[Models.NTamd64.10.0...22000]\n" +
                "%GPU%=Install," + NvidiaA + "\n");
        }
        stream.Position = 0;
        var comparison = LocalOemDriverComparison.Compare(
            stream, devices, "x64", 10, 0, 26100);
        Assert.Empty(comparison.Findings);
        Assert.Empty(comparison.MatchedDeviceOrdinals);
        Assert.Equal(1, comparison.ExcludedAmbiguousPnpInstances);
        Assert.False(comparison.IsInstallable);

        var copy = ScanCoverageText.Create(
            report, coverage.Rows, devices.Length, 0, 0,
            devices, true, vendorCoverage: coverage);
        Assert.DoesNotContain("PRIVATE", copy);
        Assert.DoesNotContain(NvidiaA, copy);
        Assert.DoesNotContain(NvidiaB, copy);
        Assert.DoesNotContain(WuaGuid, copy);
    }

    [Fact]
    public void AnUnrelatedGoodKillerInstanceIsNotDiscardedWithBadGpu()
    {
        var gpuA = Device("PRIVATE_GPU", NvidiaA);
        var gpuB = Device("private_gpu", NvidiaB);
        var killer = Device("PRIVATE_KILLER", KillerA, "Killer AX1675");
        var selected = PnpDistinctTrustedDevices.Create(
            [gpuA, gpuB, killer]);
        Assert.Same(killer, Assert.Single(selected));

        var audit = WuaPnpAssociationAudit.Create(
            [gpuA, gpuB, killer], Report(KillerA), true);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
        Assert.Equal(0, audit.Unattributed);
    }

    [Fact]
    public void IdenticalConcreteModelsAndTheirSingleOemSubsysStillWork()
    {
        var bare = Device("PRIVATE_GPU", NvidiaA);
        var same = Device("private_gpu", NvidiaA);
        var qualified = Device("PRIVATE_GPU", NvidiaOemA);
        Assert.Same(same, Assert.Single(
            PnpDistinctTrustedDevices.Create([same, bare])));
        var chosen = Assert.Single(
            PnpDistinctTrustedDevices.Create([bare, qualified]));
        Assert.Same(qualified, chosen);
        Assert.Same(qualified, Assert.Single(
            PnpDistinctTrustedDevices.Create([qualified, bare])));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            chosen, Update(NvidiaOemA)));
    }

    [Fact]
    public void CompatibleModelAloneDoesNotOverruleGenuineHardwareModel()
    {
        var first = Device("PRIVATE_GPU", NvidiaA);
        var compatibleOnly = Device("private_gpu", NvidiaA) with
        {
            CompatibleIds = [NvidiaB]
        };
        // Compatible IDs can contain multiple matching fallbacks. Only
        // the explicit HardwareIds prove contradictory device models.
        Assert.Same(first, Assert.Single(
            PnpDistinctTrustedDevices.Create([first, compatibleOnly])));
    }

    [Fact]
    public void OnlyGenericFirstWithAmbiguousModelsIsNeverExact()
    {
        var generic = Device("PRIVATE_GPU", @"PCI\VEN_10DE&CC_0300");
        var one = Device("private_gpu", NvidiaA);
        var two = Device("PRIVATE_GPU", NvidiaB);
        var chosen = Assert.Single(
            PnpDistinctTrustedDevices.Create([generic, one, two]));
        Assert.Same(generic, chosen);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            chosen, Update(NvidiaA)));
    }
}
