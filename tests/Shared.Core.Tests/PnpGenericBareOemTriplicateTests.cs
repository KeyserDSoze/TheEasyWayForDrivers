using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #444: Windows snapshots for the same PnP instance can contain generic,
/// PCI VEN+DEV and PCI VEN+DEV+SUBSYS rows. Promote only a genuine,
/// unambiguous qualified Hardware ID, never invent a driver or PnP ID.
/// </summary>
public sealed class PnpGenericBareOemTriplicateTests
{
    private const string NvidiaBase = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaOem = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string KillerBase = @"PCI\VEN_8086&DEV_51F1";
    private const string KillerOem = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string RealtekBase = @"PCI\VEN_10EC&DEV_8125";
    private const string RealtekOem = @"PCI\VEN_10EC&DEV_8125&SUBSYS_012310EC";
    private const string UpdateGuid = "00000000-0000-4000-8000-000000000444";

    private static DriverInfo Device(string instance, string hardware) =>
        new(instance, "Windows generic device", "Microsoft", "Unknown",
            "Microsoft", "31.0.0.1", null, "oem42.inf", true, true, 0,
            [hardware], []);

    private static DriverUpdateInfo Update(string hardware) =>
        new(UpdateGuid, "Windows driver", null, "Unknown", "Microsoft",
            null, null, false, "Microsoft", null, hardware, null,
            SearchSource: "Windows Update online");

    private static DriverSearchReport Report(string hardware) =>
        new([Update(hardware)],
            [new DriverSearchPassStatus(
                "Windows Update online", "Completed", 1, "OK")]);

    [Theory]
    [InlineData(@"PCI\VEN_10DE&CC_0300", NvidiaBase, NvidiaOem)]
    [InlineData(@"PCI\CC_0300", NvidiaBase, NvidiaOem)]
    [InlineData(@"PCI\VEN_8086&CC_0280", KillerBase, KillerOem)]
    [InlineData(@"PCI\CC_0200", RealtekBase, RealtekOem)]
    public void GenericPlusBarePlusOneOemModelSelectsActualQualifiedRow(
        string generic, string bare, string precise)
    {
        var weak = Device("PRIVATE_SAME_INSTANCE", generic);
        var middle = Device("private_same_instance", bare);
        var qualified = Device("PRIVATE_SAME_INSTANCE", precise);
        Assert.NotNull(PnpKnownVendorIdentity.FromId(bare));

        foreach (var sources in new[]
        {
            new[] { weak, middle, qualified },
            new[] { weak, qualified, middle }
        })
        {
            var selected = Assert.Single(
                PnpDistinctTrustedDevices.Create(sources));
            Assert.Same(qualified, selected);
            Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
                selected, Update(precise)));
            Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
                weak, Update(precise)));
            Assert.Equal(1, WuaPnpAssociationAudit.Create(
                sources, Report(precise), true).ExactHardware);
        }
    }

    [Fact]
    public void SameInstanceGpuAlsoReachesHomeAndReadOnlyOemInfComparison()
    {
        var generic = Device("PRIVATE_GPU", @"PCI\VEN_10DE&CC_0300");
        var bare = Device("private_gpu", NvidiaBase);
        var precise = Device("PRIVATE_GPU", NvidiaOem);
        var all = new[] { generic, bare, precise };
        var home = HomeVendorCoverageSummary.Create(all, Report(NvidiaOem),
            true);
        Assert.Contains("1 candidato WUA per ID", home[0].WindowsStatus);
        Assert.Equal(precise,
            HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                all, "NVIDIA", true));

        using var zipFile = new MemoryStream();
        using (var zip = new ZipArchive(zipFile, ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            using var writer = new StreamWriter(
                zip.CreateEntry("driver/nvidia.inf").Open(),
                new UTF8Encoding(false));
            writer.Write("[Version]\nSignature=\"$Windows NT$\"\n" +
                "DriverVer=10/04/2026,32.0.15.7000\n" +
                "[Manufacturer]\n%GPU%=Models,NTamd64.10.0...22000\n" +
                "[Models.NTamd64.10.0...22000]\n" +
                "%GPU%=Install," + NvidiaOem + "\n");
        }
        zipFile.Position = 0;
        var oem = LocalOemDriverComparison.Compare(zipFile, all,
            "x64", 10, 0, 26100);
        Assert.Single(oem.Findings);
        Assert.Equal([0], oem.MatchedDeviceOrdinals);
        Assert.Equal([0], oem.PnpMatchedDeviceOrdinals);
        Assert.True(oem.Findings[0].HasExactHardwareId);
        Assert.False(oem.IsInstallable);
        Assert.False(oem.IsVerifiedCompatible);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_1F15")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1")]
    [InlineData(@"USB\VID_0955&PID_7019")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA\PRIVATE")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_NOT_HEX")]
    public void ConflictingOrMalformedLaterRowCannotPromoteOem(
        string conflicting)
    {
        var weak = Device("PRIVATE_GPU", @"PCI\VEN_10DE&CC_0300");
        var bare = Device("private_gpu", NvidiaBase);
        var qualified = Device("PRIVATE_GPU", NvidiaOem);
        var extra = Device("private_gpu", conflicting);
        var selected = PnpDistinctTrustedDevices.Create(
            [weak, bare, qualified, extra]);

        Assert.DoesNotContain(selected, row => ReferenceEquals(row, qualified));
        Assert.All(selected, row => Assert.Equal(0,
            DriverUpdateMatcher.MatchStrength(row, Update(NvidiaOem))));
    }

    [Fact]
    public void TwoSubsysVariantsFailClosedEvenWithIdenticalBarePciModel()
    {
        var generic = Device("PRIVATE_GPU", @"PCI\VEN_10DE&CC_0300");
        var bare = Device("private_gpu", NvidiaBase);
        var oemA = Device("PRIVATE_GPU", NvidiaOem);
        var oemB = Device("private_gpu",
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A21043");

        var selected = Assert.Single(
            PnpDistinctTrustedDevices.Create(
                [generic, bare, oemA, oemB]));
        Assert.Same(generic, selected);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            selected, Update(NvidiaOem)));
    }

    [Fact]
    public void QualifiedCompatibleIdNeverBecomesHardwareEvidence()
    {
        var weak = Device("PRIVATE_GPU", @"PCI\VEN_10DE&CC_0300");
        var bare = Device("private_gpu", NvidiaBase) with
        {
            CompatibleIds = [NvidiaOem]
        };
        var qualified = Device("private_gpu", NvidiaOem) with
        {
            HardwareIds = [NvidiaBase],
            CompatibleIds = [NvidiaOem]
        };
        Assert.Same(weak, Assert.Single(
            PnpDistinctTrustedDevices.Create([weak, bare, qualified])));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            weak, Update(NvidiaOem)));
    }

    [Fact]
    public void DifferentPnpInstancesCannotLendEachOtherOemHardware()
    {
        var weak = Device("PRIVATE_GPU_A", @"PCI\VEN_10DE&CC_0300");
        var bare = Device("private_gpu_a", NvidiaBase);
        var qualified = Device("PRIVATE_GPU_B", NvidiaOem);
        var selected = PnpDistinctTrustedDevices.Create(
            [weak, bare, qualified]);

        Assert.Equal(2, selected.Count);
        Assert.Same(bare, selected[0]);
        Assert.Same(qualified, selected[1]);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            selected[0], Update(NvidiaOem)));
    }

    [Fact]
    public void UnknownPciVendorDoesNotGainSubsysAuthority()
    {
        var generic = Device("PRIVATE_OTHER", @"PCI\CC_0300");
        var bare = Device("private_other", @"PCI\VEN_9999&DEV_1234");
        var qualified = Device("PRIVATE_OTHER",
            @"PCI\VEN_9999&DEV_1234&SUBSYS_12345678");
        var selected = Assert.Single(PnpDistinctTrustedDevices.Create(
            [generic, bare, qualified]));
        Assert.Same(generic, selected);
    }

    [Fact]
    public void ConflictingCompatibleModelBlocksPromotion()
    {
        var generic = Device("PRIVATE_GPU", @"PCI\VEN_10DE&CC_0300");
        var bare = Device("private_gpu", NvidiaBase) with
        {
            CompatibleIds = [@"PCI\VEN_10DE&DEV_1F15"]
        };
        var qualified = Device("PRIVATE_GPU", NvidiaOem);
        Assert.Same(generic, Assert.Single(
            PnpDistinctTrustedDevices.Create([generic, bare, qualified])));
    }

    [Fact]
    public void FirstCompleteHardwareRowRemainsAuthoritative()
    {
        var specific = Device("PRIVATE_GPU", NvidiaBase);
        var generic = Device("private_gpu", @"PCI\VEN_10DE&CC_0300");
        var qualified = Device("PRIVATE_GPU", NvidiaOem);
        Assert.Same(qualified, Assert.Single(
            PnpDistinctTrustedDevices.Create([specific, generic, qualified])));
    }
}
