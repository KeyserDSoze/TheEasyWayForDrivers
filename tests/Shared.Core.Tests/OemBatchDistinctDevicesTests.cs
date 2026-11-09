using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Cross-archive dedup uses an ephemeral ordinal assigned from the exact
/// bounded local PnP inventory. Display names are never identity keys,
/// and the device ID/serial/order number may never enter batch output.
/// </summary>
public sealed class OemBatchDistinctDevicesTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string GpuCompatible = @"PCI\VEN_10DE&DEV_28A0";
    private const string OtherGpu = @"PCI\VEN_10DE&DEV_28A1";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(
        string id, string name, string hardware, string installed = "31.0.0.0",
        IReadOnlyList<string>? compatible = null) =>
        new(id, name, "Microsoft", "Display", "Microsoft", installed,
            null, "oem3.inf", true, true, 0, [hardware], compatible ?? []);

    private static string Inf(
        IEnumerable<string> ids, string version,
        string decoration = "NTamd64.10.0...22000")
    {
        var models = ids.Select((id, i) => $"Dev{i}=Install,{id}");
        return "[Version]\nSignature=\"$Windows NT$\"\n" +
               $"DriverVer=10/04/2026,{version}\n" +
               "[Manufacturer]\nVendor=Models," + decoration + "\n" +
               $"[Models.{decoration}]\n" +
               string.Join("\n", models) + "\n";
    }

    private static MemoryStream Zip(string archiveInfName, string inf)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            var entry = zip.CreateEntry(archiveInfName, CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(inf);
        }
        stream.Position = 0;
        return stream;
    }

    private static LocalOemDriverComparison.Report ReadZip(
        MemoryStream stream, IReadOnlyList<DriverInfo> devices) =>
        LocalOemDriverComparison.Compare(stream, devices, "x64", 10, 0, 26100);

    [Fact]
    public void DuplicateNvidiaGpuAcrossArchivesBecomesOneRowWithBestNumericVersion()
    {
        var devices = new[]
        {
            Device("PRIVATE_GPU_DEVICE_INSTANCE", "Microsoft Basic Display Adapter",
                Gpu, "31.0.0.0")
        };
        using var older = Zip("order-991122/GPU-old.inf",
            Inf([Gpu], "32.0.9.9999"));
        using var newer = Zip("order-991122/GPU-new.inf",
            Inf([Gpu], "32.0.10.1"));
        var first = ReadZip(older, devices);
        var second = ReadZip(newer, devices);

        Assert.Equal([0], first.MatchedDeviceOrdinals);
        Assert.Equal([0], second.MatchedDeviceOrdinals);
        var result = LocalOemArchiveBatchSummary.Combine([first, second]);
        var winner = Assert.Single(result.Findings);
        Assert.Equal(1, result.DistinctMatchedDevices);
        Assert.Equal(2, result.TotalAssociations);
        Assert.Equal(1, result.RepeatedAssociations);
        Assert.Equal(0, result.RowsOmitted);
        Assert.Equal(1, result.PotentialNewerShown);
        Assert.Equal("32.0.10.1", winner.PackageVersion);
        Assert.StartsWith("Archivio 02", winner.InfName);
        Assert.Contains("presente in 2 archivi", winner.InfName);
        Assert.Equal(0, winner.DeviceOrdinal);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerifiedCompatible);
        Assert.DoesNotContain("991122", result.Summary);
        Assert.DoesNotContain("PRIVATE", result.Summary);
        Assert.DoesNotContain("PRIVATE", winner.InfName);
        Assert.DoesNotContain("991122", winner.InfName);
    }

    [Fact]
    public void TwoDevicesWithIdenticalFriendlyNamesAreNeverIncorrectlyMerged()
    {
        var devices = new[]
        {
            Device("PRIVATE_NVIDIA_GPU_1", "Microsoft Basic Display Adapter",
                Gpu),
            Device("PRIVATE_NVIDIA_GPU_2", "Microsoft Basic Display Adapter",
                OtherGpu)
        };
        using var a = Zip("first.inf", Inf([Gpu], "33.0.0.0"));
        using var b = Zip("second.inf", Inf([OtherGpu], "33.0.0.0"));
        var result = LocalOemArchiveBatchSummary.Combine(
            [ReadZip(a, devices), ReadZip(b, devices)]);

        Assert.Equal(2, result.DistinctMatchedDevices);
        Assert.Equal(0, result.RepeatedAssociations);
        Assert.Equal(2, result.TotalAssociations);
        Assert.Equal(2, result.Findings.Count);
        Assert.Equal(2, result.Findings.Select(f => f.DeviceOrdinal).Distinct().Count());
        Assert.All(result.Findings, f =>
            Assert.Equal("Microsoft Basic Display Adapter", f.DeviceName));
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void PartialIntelPackCannotHideLaterNvidiaKillerAndRealtek()
    {
        var intel = Enumerable.Range(0, 110).Select(i =>
            Device("SECRET_INTEL_" + i, "Intel Network " + i,
                $@"PCI\VEN_8086&DEV_{i:X4}", "30.0.0.0")).ToArray();
        var special = new[]
        {
            Device("SECRET_GPU", "Microsoft Basic Display Adapter", Gpu),
            Device("SECRET_KILLER", "Killer WiFi", Killer),
            Device("SECRET_AUDIO", "High Definition Audio Device", Realtek)
        };
        var devices = intel.Concat(special).ToArray();

        using var intelZip = Zip("PCSpecialist_ORD123/Intel.inf",
            Inf(intel.Select(d => d.HardwareIds[0]), "32.0.0.0"));
        using var gpuZip = Zip("PCSpecialist_ORD123/NVIDIA.inf",
            Inf([Gpu, Killer], "32.0.0.0"));
        using var realtekZip = Zip("PCSpecialist_ORD123/Realtek.inf",
            Inf([Realtek], "32.0.0.0"));

        var first = ReadZip(intelZip, devices);
        var second = ReadZip(gpuZip, devices);
        var third = ReadZip(realtekZip, devices);
        Assert.Equal(80, first.Findings.Count);
        Assert.Equal(30, first.MatchingDevicesOmitted);
        Assert.Equal(110, first.MatchedDeviceOrdinals!.Count);
        var batch = LocalOemArchiveBatchSummary.Combine([first, second, third]);

        Assert.Equal(113, batch.DistinctMatchedDevices);
        Assert.Equal(113, batch.TotalAssociations);
        Assert.Equal(0, batch.RepeatedAssociations);
        Assert.Equal(80, batch.Findings.Count);
        Assert.Equal(33, batch.RowsOmitted);
        Assert.Contains(batch.Findings, f => f.DeviceName ==
            "Microsoft Basic Display Adapter" && f.IsPotentialNewer);
        Assert.Contains(batch.Findings, f => f.DeviceName ==
            "Killer WiFi" && f.IsPotentialNewer);
        Assert.Contains(batch.Findings, f => f.DeviceName ==
            "High Definition Audio Device" && f.IsPotentialNewer);
        Assert.Contains(batch.Notes, note => note.Contains("parziale"));
        Assert.Contains("113 dispositivi distinti", batch.Summary);
        Assert.DoesNotContain("ORD123", batch.Summary);
        Assert.All(batch.Findings, f => Assert.DoesNotContain("ORD123", f.InfName));
    }

    [Fact]
    public void TwentyFourCopiesOfSameGpuLeaveTwentyThreeExtraAssociationsAndOneVisibleDevice()
    {
        var devices = new[] { Device("PRIVATE_GPU", "RTX Laptop", Gpu) };
        var reports = new List<LocalOemDriverComparison.Report>();
        using var stream = Zip("nvidia.inf", Inf([Gpu], "32.0.0.0"));
        for (int i = 0; i < LocalOemArchiveBatchSummary.MaximumArchives; i++)
        {
            stream.Position = 0;
            reports.Add(ReadZip(stream, devices));
        }
        var batch = LocalOemArchiveBatchSummary.Combine(reports);

        Assert.Single(batch.Findings);
        Assert.Equal(1, batch.DistinctMatchedDevices);
        Assert.Equal(LocalOemArchiveBatchSummary.MaximumArchives,
            batch.TotalAssociations);
        Assert.Equal(LocalOemArchiveBatchSummary.MaximumArchives - 1,
            batch.RepeatedAssociations);
        Assert.Equal(0, batch.RowsOmitted);
        Assert.Equal(1, batch.PotentialNewerShown);
        Assert.Contains("presente in 24 archivi", batch.Findings[0].InfName);
        Assert.False(batch.IsInstallable);
    }

    [Fact]
    public void ExactIdAndMatchingWindowsOutrankInflatedGenericOrArm64Versions()
    {
        var devices = new[] { Device("PRIVATE_GPU", "RTX Laptop",
            Gpu, "31.0.0.0", [GpuCompatible]) };
        using var weakZip = Zip("generic.inf", Inf([GpuCompatible], "99.0.0.0"));
        using var wrongZip = Zip("arm64.inf", Inf([Gpu], "99.0.0.0",
            "NTarm64.10.0...22000"));
        using var goodZip = Zip("official.inf", Inf([Gpu], "32.0.0.0"));
        var batch = LocalOemArchiveBatchSummary.Combine(
        [
            ReadZip(weakZip, devices),
            ReadZip(wrongZip, devices),
            ReadZip(goodZip, devices)
        ]);
        var winner = Assert.Single(batch.Findings);
        Assert.Equal("32.0.0.0", winner.PackageVersion);
        Assert.True(winner.HasExactHardwareId);
        Assert.True(winner.IsPotentialNewer);
        Assert.StartsWith("Archivio 03", winner.InfName);
        Assert.Equal(1, batch.DistinctMatchedDevices);
        Assert.Equal(2, batch.RepeatedAssociations);
        Assert.False(batch.IsInstallable);
    }

    [Fact]
    public void CorruptOrInconsistentOrdinalEvidenceFailsClosed()
    {
        var fake = new LocalOemDriverComparison.Finding(
            "NVIDIA", "private.inf", "31.0.0.0", "32.0.0.0",
            2, InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            LocalOemDriverComparison.VersionRelation.NewerHint)
        { DeviceOrdinal = 2 };
        var inconsistent = new LocalOemDriverComparison.Report(
            1, 2, [fake], 0, [])
        { MatchedDeviceOrdinals = [0] };
        Assert.Throws<InvalidDataException>(() =>
            LocalOemArchiveBatchSummary.Combine([inconsistent]));

        var valid = inconsistent with
        {
            Findings = [fake with { DeviceOrdinal = 0 }]
        };
        var legacy = valid with { MatchedDeviceOrdinals = null };
        Assert.Throws<InvalidDataException>(() =>
            LocalOemArchiveBatchSummary.Combine([valid, legacy]));
    }

    [Fact]
    public void LegacyReportsWithoutOrdinalsDoNotClaimDistinctDeviceCounts()
    {
        var old = new LocalOemDriverComparison.Finding(
            "High Definition Audio Device", "private.inf",
            "31.0.0.0", "32.0.0.0", 2,
            InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            LocalOemDriverComparison.VersionRelation.NewerHint);
        var source = new LocalOemDriverComparison.Report(
            1, 2, [old], 0, []);
        var batch = LocalOemArchiveBatchSummary.Combine([source, source]);
        Assert.Null(batch.DistinctMatchedDevices);
        Assert.Null(batch.RepeatedAssociations);
        Assert.Equal(2, batch.Findings.Count);
        Assert.Contains("NON verificabile", batch.Summary);
        Assert.False(batch.IsInstallable);
    }
}
