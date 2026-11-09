using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Review-only, OEM SUBSYS-preserving INF correlation. PnP may report
/// SUBSYS+REV but omit its normal SUBSYS-only compatible ID; stripping
/// REV is not authority to install or claim a newer Windows driver.
/// </summary>
public sealed class OemSubsysRevisionReviewTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string GpuRevision = Gpu + "&REV_A1";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Realtek =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295&SUBSYS_000010EC";
    private const string IntelAudio =
        @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295&SUBSYS_000010EC";
    private const string NvidiaSwc = @"SWC\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";

    private static DriverInfo Device(string hardware,
        IReadOnlyList<string>? compatible = null, string? installedVersion = "30.0.0.0") =>
        new("PRIVATE_INSTANCE_SERIAL_A123", "Generic Windows Device",
            "Microsoft", "Unknown", "Microsoft", installedVersion,
            null, "oem1.inf", true, true, 0, [hardware], compatible ?? []);

    private static string Inf(string hardware, string version = "99.0.0.0",
        string? additional = null) => $"""
        [Version]
        Signature="$Windows NT$"
        Class=Display
        DriverVer=10/10/2026,{version}
        [Manufacturer]
        %Vendor%=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        %Device%=Install,{hardware}{(additional is null ? "" : "," + additional)}
        """;

    private static MemoryStream Zip(params (string Path, string Inf)[] files)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, inf) in files)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(inf);
            }
        memory.Position = 0;
        return memory;
    }

    [Theory]
    [InlineData(Gpu, GpuRevision)]
    [InlineData(Killer, Killer + "&REV_02")]
    [InlineData(Realtek, Realtek + "&REV_01")]
    [InlineData(IntelAudio, IntelAudio + "&REV_0A")]
    [InlineData(NvidiaSwc, NvidiaSwc + "&REV_0B")]
    public void ExactSubsysWithoutTerminalRevisionIsReviewOnly(
        string declared, string installed)
    {
        var pc = Device(installed);
        var content = Inf(declared);
        Assert.Empty(InfModelEvidence.ReadMatchingIds(content, [pc]));
        Assert.Empty(InfModelEvidence.ReadPublicModelReviewIds(content, [pc]));

        var review = InfModelEvidence.ReadRevisionlessSubsysReviewIds(
            content, [pc]);
        Assert.Equal(declared, Assert.Single(review).HardwareId);
        Assert.True(InfModelEvidence.IsRevisionlessSubsysReviewMatch(pc, review));
        Assert.Equal(0, InfModelEvidence.MatchStrength(pc, review));

        using var zip = Zip(("oem/subsys.inf", content));
        var report = LocalOemDriverComparison.Compare(
            zip, [pc], "x64", 10, 0, 26100);
        var row = Assert.Single(report.Findings);
        Assert.True(row.IsSubsysRevisionReview);
        Assert.True(row.IsModelOnlyReview);
        Assert.False(row.HasExactHardwareId);
        Assert.Equal(0, row.HardwareMatchStrength);
        Assert.Equal(InfPlatformPreflight.Verdict.ManualReviewRequired,
            row.PlatformVerdict);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.Unavailable,
            row.VersionComparison);
        Assert.False(row.IsPotentialNewer);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.Equal(1, report.SubsysRevisionReviewDevices);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.Contains("SOLO SUBSYS OEM senza REV", row.Summary);
        Assert.Contains("SUBSYS uguale", report.Summary);
        Assert.DoesNotContain(installed, row.Summary);
        Assert.DoesNotContain("PRIVATE_INSTANCE_SERIAL", report.Summary);
    }

    [Theory]
    [InlineData(Gpu, @"PCI\VEN_10DE&DEV_28A0&SUBSYS_00000000")]
    [InlineData(Gpu, @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AB")]
    [InlineData(Gpu, @"PCI\VEN_10DE&DEV_28A1&SUBSYS_16A217AA")]
    [InlineData(Gpu, @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA&REV_A0")]
    [InlineData(Gpu, @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA&UID_01")]
    [InlineData(Gpu, @"PCI\CC_0300")]
    public void OtherSubsysOrModelDoesNotCreateRevisionlessReview(
        string model, string declared)
    {
        var pc = Device(model + "&REV_A1");
        var inf = Inf(declared);
        Assert.Empty(InfModelEvidence.ReadRevisionlessSubsysReviewIds(
            inf, [pc]));
        using var zip = Zip(("not-my-model.inf", inf));
        Assert.Empty(LocalOemDriverComparison.Compare(zip, [pc],
            "x64", 10, 0, 26100).Findings);
    }

    [Theory]
    [InlineData(Gpu + "&REV_A")]
    [InlineData(Gpu + "&REV_A12")]
    [InlineData(Gpu + "&REV_A1&UID_01")]
    [InlineData(Gpu + "&REV_ZZ")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&REV_A1")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA&CC_030000&REV_A1")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA\SERIAL")]
    public void InvalidRevisionOrNonterminalShapeFailsClosed(
        string hardware)
    {
        var pc = Device(hardware);
        Assert.Empty(InfModelEvidence.ReadRevisionlessSubsysReviewIds(
            Inf(Gpu), [pc]));
    }

    [Fact]
    public void ReviewDoesNotUseCompatibleOnlyOrInfSecondaryAlias()
    {
        var compatibleOnly = Device(@"ROOT\GENERIC", [GpuRevision]);
        var inf = Inf(Gpu);
        Assert.Empty(InfModelEvidence.ReadRevisionlessSubsysReviewIds(
            inf, [compatibleOnly]));

        var aliasInf = Inf(@"PCI\VEN_1002&DEV_164E", additional: Gpu);
        var device = Device(GpuRevision);
        Assert.Empty(InfModelEvidence.ReadRevisionlessSubsysReviewIds(
            aliasInf, [device]));
        using var zip = Zip(("secondary.inf", aliasInf));
        Assert.Empty(LocalOemDriverComparison.Compare(
            zip, [device], "x64", 10, 0, 26100).Findings);
    }

    [Fact]
    public void MatchingSubsysInUnreferencedModelsSectionIsIgnored()
    {
        var text = Inf(@"PCI\VEN_1002&DEV_164E") +
            "\n[Models.NTarm64]\nDevice=Install," + Gpu;
        var device = Device(GpuRevision);
        Assert.Empty(InfModelEvidence.ReadRevisionlessSubsysReviewIds(
            text, [device]));
        using var zip = Zip(("undeclared.inf", text));
        Assert.Empty(LocalOemDriverComparison.Compare(
            zip, [device], "x64", 10, 0, 26100).Findings);
    }

    [Fact]
    public void ExplicitCompatibleWithUncomparableVersionOutranksNewerReview()
    {
        // Undecorated Models sections have unknown Windows applicability,
        // so both the genuine compatible match and the review hint receive
        // ManualReviewRequired. Strength 1 must still outrank strength 0.
        static string Undecorated(string id, string version) =>
            Inf(id, version)
                .Replace("%Vendor%=Models,NTamd64.10.0...22000",
                    "%Vendor%=Models", StringComparison.Ordinal)
                .Replace("[Models.NTamd64.10.0...22000]",
                    "[Models]", StringComparison.Ordinal);

        using var archive = Zip(
            ("a-review.inf", Undecorated(Gpu, "99.0.0.0")),
            ("z-compatible.inf", Undecorated(
                @"PCI\VEN_10DE&DEV_28A0", "unversioned")));
        var pc = Device(GpuRevision,
            [@"PCI\VEN_10DE&DEV_28A0"], installedVersion: "30.0.0.0");
        var report = LocalOemDriverComparison.Compare(
            archive, [pc], "x64", 10, 0, 26100);
        var row = Assert.Single(report.Findings);
        Assert.Equal(1, row.HardwareMatchStrength);
        Assert.Equal("z-compatible.inf", row.InfName);
        Assert.False(row.IsModelOnlyReview);
    }

    [Fact]
    public void TrueCompatibleOutranksSubsysReviewAcrossArchives()
    {
        // Force equal platform/version evidence: the report merger must
        // always prefer explicit PnP evidence over a numerically higher
        // but review-only INF model, regardless of input archive order.
        var review = new LocalOemDriverComparison.Finding(
            "Generic GPU", "review.inf", "N/D", "99.0.0.0", 0,
            InfPlatformPreflight.Verdict.ManualReviewRequired,
            LocalOemDriverComparison.VersionRelation.Unavailable)
        {
            DeviceOrdinal = 0,
            IsSubsysRevisionReview = true
        };
        var compatible = new LocalOemDriverComparison.Finding(
            "Generic GPU", "compatible.inf", "N/D", "1.0.0.0", 1,
            InfPlatformPreflight.Verdict.ManualReviewRequired,
            LocalOemDriverComparison.VersionRelation.Unavailable)
        {
            DeviceOrdinal = 0
        };
        var weak = new LocalOemDriverComparison.Report(
            1, 1, [review], 0, [])
        {
            MatchedDeviceOrdinals = [0]
        };
        var strong = new LocalOemDriverComparison.Report(
            1, 1, [compatible], 0, [])
        {
            MatchedDeviceOrdinals = [0]
        };
        var combined = LocalOemArchiveBatchSummary.Combine([weak, strong]);
        var row = Assert.Single(combined.Findings);
        Assert.Equal(1, row.HardwareMatchStrength);
        Assert.False(row.IsModelOnlyReview);
        Assert.Equal(0, combined.ModelOnlyReviewShown);
        Assert.Equal(0, combined.SubsysRevisionReviewShown);
        Assert.Equal(1, combined.DistinctMatchedDevices);
        Assert.False(combined.IsInstallable);
        Assert.DoesNotContain("PRIVATE_INSTANCE_SERIAL", combined.Summary);
    }

    [Fact]
    public void SubsysReviewOutranksPublicModelReviewIndependentOfVersion()
    {
        var device = Device(GpuRevision);
        using var zip = Zip(
            ("a-public.inf", Inf(@"PCI\VEN_10DE&DEV_28A0", "99.0.0.0")),
            ("z-subsys.inf", Inf(Gpu, "1.0.0.0")));
        var result = LocalOemDriverComparison.Compare(
            zip, [device], "x64", 10, 0, 26100);
        var row = Assert.Single(result.Findings);
        Assert.True(row.IsSubsysRevisionReview);
        Assert.Equal("z-subsys.inf", row.InfName);
        Assert.Equal(0, result.PotentialNewerDevices);
        Assert.Contains("SUBSYS", result.Summary);
    }

    [Fact]
    public void LateSameSubsysModelAfterFiveHundredInfIdsIsNotLost()
    {
        var unrelated = Enumerable.Range(1, 560).Select(i =>
            $"Other{i}=Install,PCI\\VEN_9999&DEV_{i:X4}");
        var inf = Inf(@"PCI\VEN_9999&DEV_0000") +
            "\n[Models.NTamd64.10.0...22000]\n" +
            string.Join("\n", unrelated) + "\nGpu=Install," + Gpu;
        var device = Device(GpuRevision);
        Assert.Equal(InfModelEvidence.MaximumPreviewIds,
            InfModelEvidence.ReadDeclaredIds(inf).Count);
        var hint = Assert.Single(
            InfModelEvidence.ReadRevisionlessSubsysReviewIds(inf, [device]));
        Assert.Equal(Gpu, hint.HardwareId);

        using var zip = Zip(("large.inf", inf));
        var result = LocalOemDriverComparison.Compare(
            zip, [device], "x64", 10, 0, 26100);
        Assert.True(Assert.Single(result.Findings).IsSubsysRevisionReview);
        Assert.Equal(0, result.PotentialNewerDevices);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void FolderAndArchiveBatchKeepReviewOnlyWithoutDriverUpdates()
    {
        var temp = Directory.CreateTempSubdirectory("OmegaDrive-SubsysReview-");
        var pc = Device(GpuRevision);
        try
        {
            File.WriteAllText(Path.Combine(temp.FullName, "gpu.inf"), Inf(Gpu));
            var folderReport = LocalOemFolderComparison.Compare(
                temp.FullName, [pc], "x64", 10, 0, 26100);
            Assert.True(Assert.Single(folderReport.Findings).IsSubsysRevisionReview);

            using var zip = Zip(("gpu.inf", Inf(Gpu)));
            var zipReport = LocalOemDriverComparison.Compare(
                zip, [pc], "x64", 10, 0, 26100);
            var combo = LocalOemArchiveBatchSummary.Combine(
                [folderReport, zipReport]);
            Assert.Equal(1, combo.DistinctMatchedDevices);
            Assert.Equal(1, combo.SubsysRevisionReviewShown);
            Assert.Equal(0, combo.PotentialNewerShown);
            Assert.False(combo.IsInstallable);
            Assert.False(combo.IsVerifiedCompatible);
            Assert.Contains("SUBSYS senza REV", combo.Summary);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }
}
