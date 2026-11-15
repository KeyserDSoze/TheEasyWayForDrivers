using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #454: the PnP consensus gate must check multiple primary HardwareIds
/// inside ONE WMI row, not just across repeated rows of the same DeviceId.
/// This is read-only association evidence, never install authorization.
/// </summary>
public sealed class PnpSingleRowModelConflictTests
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
        "00000000-0000-4000-8000-000000000454";

    private static DriverInfo Device(string instance, params string[] hardware) =>
        new(instance, "Windows generic device", "Microsoft", "Unknown",
            "Microsoft", "1.0.0.0", null, "PRIVATE_oem42.inf",
            true, true, 0, hardware, []);

    private static DriverUpdateInfo Update(string id) =>
        new(GuidId, "Windows Type Driver", null, "Unknown", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    private static DriverSearchReport Report(string id) =>
        new([Update(id)], [new DriverSearchPassStatus(
            "Windows Update online", "Completed", 1, "verified")]);

    [Theory]
    [InlineData(NvidiaA, NvidiaB)]
    [InlineData(KillerA, KillerB)]
    [InlineData(RealtekA, RealtekB)]
    [InlineData(NvidiaAudioA, NvidiaAudioB)]
    public void SingleRowWithTwoPrimaryModelsIsNeverTrusted(
        string modelA, string modelB)
    {
        var ambiguous = Device("PRIVATE_SAME_INSTANCE", modelA, modelB)
            with { CompatibleIds = [modelA] };
        var rows = new[] { ambiguous };

        // Two actual, validated HardwareIds contradict one another.
        // Compatible IDs, friendly names and provider cannot rescue it.
        Assert.Equal(PnpKnownVendorIdentity.FromId(modelA),
            PnpKnownVendorIdentity.FromId(modelB));
        Assert.Empty(PnpDistinctTrustedDevices.Create(rows));
        Assert.Empty(VendorSourceDiscovery.Find(rows, [Update(modelA)]));

        var audit = WuaPnpAssociationAudit.Create(
            rows, Report(modelA), true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(0, audit.CompatibleOnly);
        Assert.Equal(1, audit.Unattributed);
        Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
        Assert.False(audit.IsInstallable);
        Assert.Contains("identità hardware contraddittorie", audit.Text);
        Assert.DoesNotContain(modelA, audit.Text);
        Assert.DoesNotContain(modelB, audit.Text);
        Assert.DoesNotContain(GuidId, audit.Text);
        Assert.DoesNotContain("PRIVATE", audit.Text);
    }

    [Fact]
    public void SingleRowWithTwoOemSubsysIsDiscardedButBareModelIsAllowed()
    {
        var bad = Device("PRIVATE_GPU", NvidiaA, NvidiaOemA, NvidiaOemB);
        Assert.Empty(PnpDistinctTrustedDevices.Create([bad]));
        var audit = WuaPnpAssociationAudit.Create(
            [bad], Report(NvidiaOemA), true);
        Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(1, audit.Unattributed);

        // Different REV variants of the same one real OEM subsystem
        // still identify the same board. Do not discard normal WMI lists.
        var good = Device("PRIVATE_GOOD", NvidiaA, NvidiaOemA,
            NvidiaOemA + "&REV_A1");
        Assert.Same(good, Assert.Single(
            PnpDistinctTrustedDevices.Create([good])));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            good, Update(NvidiaOemA)));
    }

    [Fact]
    public void SingleRowContradictionCannotInflateHomeBenchmarkOrInf()
    {
        var ambiguous = Device("PRIVATE_GPU", NvidiaA, NvidiaB);
        var rows = new[] { ambiguous };
        var report = Report(NvidiaA);
        var home = HomeVendorCoverageSummary.Create(
            rows, report, inventoryComplete: true);
        Assert.All(home, item =>
        {
            Assert.Contains("0 dispositivi PnP", item.DeviceStatus);
            Assert.Contains("0 candidati WUA per ID", item.WindowsStatus);
            Assert.False(item.HasLocalDevice);
            Assert.False(item.IsInstallable);
        });
        Assert.Empty(VendorInventoryCoverage.Create(
            rows, report.Updates).Rows);
        var benchmark = ReferenceDriverCoverage.Create(rows, report);
        Assert.Equal(8, benchmark.Count);
        Assert.All(benchmark, item =>
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
    }

    [Fact]
    public void WeakAliasesAndMalformedIdsCannotVetoCoherentPrimaryModels()
    {
        var valid = Device("PRIVATE_GOOD", NvidiaA, NvidiaOemA,
            @"PCI\VEN_10DE&DEV_1F15" + "\t")
            with { CompatibleIds = [NvidiaB] };
        Assert.Same(valid, Assert.Single(
            PnpDistinctTrustedDevices.Create([valid])));
        Assert.Equal(0, WuaPnpAssociationAudit.Create(
            [valid], Report(NvidiaA), true)
            .ExcludedAmbiguousPnpInstances);
    }

    [Fact]
    public void AmbiguousRowDoesNotSuppressHealthyDifferentDevice()
    {
        var ambiguous = Device("PRIVATE_BAD", NvidiaA, NvidiaB);
        var killer = Device("PRIVATE_KILLER", KillerA);
        Assert.Same(killer, Assert.Single(
            PnpDistinctTrustedDevices.Create([ambiguous, killer])));
        var audit = WuaPnpAssociationAudit.Create(
            [ambiguous, killer], Report(KillerA), true);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(1, audit.ExcludedAmbiguousPnpInstances);
        Assert.Equal(0, audit.Unattributed);
    }
}
