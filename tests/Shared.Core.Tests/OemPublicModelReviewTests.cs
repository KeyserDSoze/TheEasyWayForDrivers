using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A generic OEM INF model can be a useful manual review hint when Windows
/// reports only a SUBSYS/REV-specific hardware ID. It cannot prove a driver
/// is compatible, newer, signed, rankable by SetupAPI or installable.
/// </summary>
public sealed class OemPublicModelReviewTests
{
    private const string GpuModel = @"PCI\VEN_10DE&DEV_28A0";
    private const string GpuDeviceId =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string KillerModel = @"PCI\VEN_8086&DEV_51F1";
    private const string KillerDeviceId =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string RealtekModel =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string RealtekDeviceId =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295&SUBSYS_000010EC";
    private const string SwcModel = @"SWC\VEN_10DE&DEV_28A0";
    private const string SwcDeviceId = @"SWC\VEN_10DE&DEV_28A0&REV_01";
    private const string AcpiModel = @"ACPI\NVDA0820";
    private const string AcpiDeviceId = @"ACPI\NVDA0820&REV_0001";

    private static DriverInfo Device(
        string specific, string name = "Generic Windows Device",
        string deviceClass = "Unknown",
        IReadOnlyList<string>? compatible = null) =>
        new("PRIVATE_INSTANCE_" + name, name, "Microsoft",
            deviceClass, "Microsoft", "30.0.0.0", null, "oem18.inf",
            true, true, 0, [specific], compatible ?? []);

    private static string Inf(
        string primaryId, string? compatibleId = null,
        string version = "32.0.16.2101") => $"""
        [Version]
        Signature="$Windows NT$"
        Class=Display
        DriverVer=11/09/2026,{version}
        [Manufacturer]
        %Vendor%=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        %Device%=Install,{primaryId}{(compatibleId is null ? "" : "," + compatibleId)}
        """;

    private static MemoryStream Zip(params (string Name, string Text)[] files)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(),
                    new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        memory.Position = 0;
        return memory;
    }

    [Theory]
    [InlineData(GpuModel, GpuDeviceId)]
    [InlineData(KillerModel, KillerDeviceId)]
    [InlineData(RealtekModel, RealtekDeviceId)]
    [InlineData(SwcModel, SwcDeviceId)]
    [InlineData(AcpiModel, AcpiDeviceId)]
    public void SpecificWindowsIdWithMissingCompatibleGetsManualReviewOnly(
        string publicModel, string specificId)
    {
        var installed = Device(specificId);
        var content = Inf(publicModel);

        Assert.Empty(InfModelEvidence.ReadMatchingIds(content, [installed]));
        var hints = InfModelEvidence.ReadPublicModelReviewIds(
            content, [installed]);
        var hint = Assert.Single(hints);
        Assert.True(hint.IsPrimaryHardwareId);
        Assert.Equal(publicModel, hint.HardwareId);
        Assert.True(InfModelEvidence.IsPublicModelOnlyReviewMatch(
            installed, hints));
        Assert.Equal(0, InfModelEvidence.MatchStrength(installed, hints));

        using var archive = Zip(("oem/driver.inf", content));
        var result = LocalOemDriverComparison.Compare(archive, [installed],
            "x64", 10, 0, 26100);

        var row = Assert.Single(result.Findings);
        Assert.True(row.IsModelOnlyReview);
        Assert.False(row.HasExactHardwareId);
        Assert.Equal(0, row.HardwareMatchStrength);
        Assert.Equal(InfPlatformPreflight.Verdict.ManualReviewRequired,
            row.PlatformVerdict);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.Unavailable,
            row.VersionComparison);
        Assert.False(row.IsPotentialNewer);
        Assert.Equal(0, result.PotentialNewerDevices);
        Assert.Equal(1, result.ModelOnlyReviewDevices);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerifiedCompatible);
        Assert.Contains("SOLO modello pubblico INF", row.Summary);
        Assert.Contains("NON compatibilità", row.Summary);
        Assert.Contains("NON compatibilità confermata", result.Summary);
        Assert.DoesNotContain("PRIVATE_INSTANCE", result.Summary);
        Assert.DoesNotContain(specificId, result.Summary);
        Assert.DoesNotContain(specificId, row.Summary);
    }

    [Fact]
    public void FiveDifferentDevicesProduceFiveReviewHintsNotFiveUpdates()
    {
        var pairs = new[]
        {
            (GpuModel, GpuDeviceId),
            (KillerModel, KillerDeviceId),
            (RealtekModel, RealtekDeviceId),
            (SwcModel, SwcDeviceId),
            (AcpiModel, AcpiDeviceId)
        };
        using var archive = Zip(pairs.Select((pair, i) =>
            ($"drivers/vendor-{i}.inf", Inf(pair.Item1))).ToArray());
        var devices = pairs.Select((pair, i) =>
            Device(pair.Item2, $"Generic {i}")).ToArray();

        var result = LocalOemDriverComparison.Compare(
            archive, devices, "x64", 10, 0, 26100);

        Assert.Equal(5, result.TotalMatchedDevices);
        Assert.Equal(5, result.ModelOnlyReviewDevices);
        Assert.All(result.Findings, finding =>
        {
            Assert.True(finding.IsModelOnlyReview);
            Assert.Equal(LocalOemDriverComparison.VersionRelation.Unavailable,
                finding.VersionComparison);
        });
        Assert.Equal(0, result.PotentialNewerDevices);
        Assert.DoesNotContain(result.Findings, f => f.IsPotentialNewer);
        Assert.Contains("SOLO modello pubblico", string.Join(" ", result.Notes));
        Assert.False(result.IsInstallable);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A1")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_00000000")]
    [InlineData(@"PCI\VEN_10DE")]
    [InlineData(@"PCI\CC_030000")]
    [InlineData(@"PCI\VEN_10DE&FAKE_28A0")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&UID_001")]
    public void OtherModelsSpecificSubsysOrClassOnlyNeverYieldReview(
        string infHardware)
    {
        var device = Device(GpuDeviceId);
        var text = Inf(infHardware);
        Assert.Empty(InfModelEvidence.ReadPublicModelReviewIds(text, [device]));
        Assert.Empty(InfModelEvidence.ReadMatchingIds(text, [device]));
        using var zip = Zip(("unrelated.inf", text));
        var report = LocalOemDriverComparison.Compare(
            zip, [device], "x64", 10, 0, 26100);
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.ModelOnlyReviewDevices);
        Assert.Equal(0, report.PotentialNewerDevices);
    }

    [Fact]
    public void GenericModelAsInfCompatibleAliasDoesNotProvePrimaryModel()
    {
        var text = Inf(@"PCI\VEN_1002&DEV_164E", GpuModel);
        var device = Device(GpuDeviceId);
        Assert.Empty(InfModelEvidence.ReadMatchingIds(text, [device]));
        Assert.Empty(InfModelEvidence.ReadPublicModelReviewIds(text, [device]));
        using var zip = Zip(("incompatible.inf", text));
        var report = LocalOemDriverComparison.Compare(
            zip, [device], "x64", 10, 0, 26100);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void GenericModelOnlyInUndeclaredModelsSectionIsNotConsidered()
    {
        var text = Inf(@"PCI\VEN_1002&DEV_164E") +
            "\n[Models.NTarm64]\nDevice=Install," + GpuModel;
        var device = Device(GpuDeviceId);
        Assert.Empty(InfModelEvidence.ReadPublicModelReviewIds(text, [device]));
        using var zip = Zip(("undeclared.inf", text));
        Assert.Empty(LocalOemDriverComparison.Compare(
            zip, [device], "x64", 10, 0, 26100).Findings);
    }

    [Fact]
    public void PnpCompatibleExactEvidenceAlwaysOutranksReviewOnlyFallback()
    {
        var pc = Device(GpuDeviceId, compatible: [GpuModel]);
        using var zip = Zip(("gpu.inf", Inf(GpuModel)));
        var report = LocalOemDriverComparison.Compare(
            zip, [pc], "x64", 10, 0, 26100);
        var row = Assert.Single(report.Findings);
        Assert.False(row.IsModelOnlyReview);
        Assert.Equal(1, row.HardwareMatchStrength);
        Assert.False(row.IsPotentialNewer);
        Assert.Equal(0, report.ModelOnlyReviewDevices);
    }

    [Fact]
    public void ExactSubsysInfOutranksModelOnlyInfIrrespectiveOfZipOrder()
    {
        var pc = Device(GpuDeviceId);
        foreach (var inverse in new[] { false, true })
        {
            var generic = ("a-generic.inf", Inf(GpuModel));
            var exact = ("z-exact.inf", Inf(GpuDeviceId));
            using var archive = inverse
                ? Zip(exact, generic)
                : Zip(generic, exact);
            var result = LocalOemDriverComparison.Compare(
                archive, [pc], "x64", 10, 0, 26100);
            var row = Assert.Single(result.Findings);

            Assert.False(row.IsModelOnlyReview);
            Assert.True(row.HasExactHardwareId);
            Assert.Equal("z-exact.inf", row.InfName);
            Assert.Equal(1, result.PotentialNewerDevices);
            Assert.Equal(0, result.ModelOnlyReviewDevices);
        }
    }

    [Fact]
    public void AfterFiveHundredUnrelatedInfModelsReviewStillFindsNvidia()
    {
        var dummy = Enumerable.Range(0, 550).Select(i =>
            $"Device{i}=Install,PCI\\VEN_9999&DEV_{i:X4}");
        var inf = Inf(@"PCI\VEN_9999&DEV_0000") +
            "\n[Models.NTamd64.10.0...22000]\n" +
            string.Join("\n", dummy) +
            "\nTarget=Install," + GpuModel;
        var installed = Device(GpuDeviceId);

        Assert.Equal(InfModelEvidence.MaximumPreviewIds,
            InfModelEvidence.ReadDeclaredIds(inf).Count);
        var review = Assert.Single(InfModelEvidence.ReadPublicModelReviewIds(
            inf, [installed]));
        Assert.Equal(GpuModel, review.HardwareId);
        using var archive = Zip(("gpu.inf", inf));
        var report = LocalOemDriverComparison.Compare(
            archive, [installed], "x64", 10, 0, 26100);
        Assert.Single(report.Findings);
        Assert.Equal(1, report.ModelOnlyReviewDevices);
        Assert.Equal(0, report.PotentialNewerDevices);
    }

    [Fact]
    public void FolderAndCrossArchiveSummaryPreserveManualOnlyStatus()
    {
        var folder = Directory.CreateTempSubdirectory(
            "OmegaDrive-OemModelReview-");
        var pc = Device(GpuDeviceId);
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "gpu.inf"),
                Inf(GpuModel));
            var fromFolder = LocalOemFolderComparison.Compare(
                folder.FullName, [pc], "x64", 10, 0, 26100);
            var result = Assert.Single(fromFolder.Findings);
            Assert.True(result.IsModelOnlyReview);
            Assert.Equal(0, fromFolder.PotentialNewerDevices);
            Assert.False(fromFolder.IsInstallable);

            using var zip = Zip(("gpu.inf", Inf(GpuModel)));
            var fromZip = LocalOemDriverComparison.Compare(
                zip, [pc], "x64", 10, 0, 26100);
            var combined = LocalOemArchiveBatchSummary.Combine(
                [fromZip, fromFolder]);
            Assert.Equal(1, combined.DistinctMatchedDevices);
            Assert.Single(combined.Findings);
            Assert.Equal(1, combined.ModelOnlyReviewShown);
            Assert.Equal(0, combined.PotentialNewerShown);
            Assert.Contains("SOLO modello pubblico INF", combined.Summary);
            Assert.False(combined.IsInstallable);
            Assert.False(combined.IsVerifiedCompatible);
            Assert.DoesNotContain(GpuDeviceId, combined.Summary);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void WithoutHardwareIdsOnlyCompatibleIdOrNameNeverCreatesReview()
    {
        var pc = Device(@"ROOT\GENERIC", name: "NVIDIA RTX 4060") with
        {
            HardwareIds = [],
            CompatibleIds = [GpuDeviceId],
            Manufacturer = "NVIDIA",
            DriverProvider = "NVIDIA"
        };
        Assert.Empty(InfModelEvidence.ReadPublicModelReviewIds(
            Inf(GpuModel), [pc]));
        using var zip = Zip(("gpu.inf", Inf(GpuModel)));
        Assert.Empty(LocalOemDriverComparison.Compare(
            zip, [pc], "x64", 10, 0, 26100).Findings);
    }
}
