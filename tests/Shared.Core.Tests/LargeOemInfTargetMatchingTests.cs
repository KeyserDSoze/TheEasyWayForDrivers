using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class LargeOemInfTargetMatchingTests
{
    private const string NvidiaGpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string KillerWifi = @"PCI\VEN_8086&DEV_51F1&SUBSYS_11118086";

    private static string LargeInf(string target, string architecture = "NTamd64",
        string driverVersion = "32.0.16.2000")
    {
        // >500 distinct, correctly formatted IDs. NVIDIA/Killer target is
        // deliberately the LAST declaration in [Models], after the old cap.
        var unrelated = Enumerable.Range(0, 620)
            .Select(i =>
                $@"%Other{i}%=Install,PCI\VEN_8086&DEV_{i:X4}&SUBSYS_99999999");
        var section = "Models." + architecture + ".10.0...22000";
        return "[Version]\nSignature=\"$Windows NT$\"\nClass=Display\n" +
            $"DriverVer=10/04/2026,{driverVersion}\n" +
            "[Manufacturer]\n%Vendor%=Models," +
            architecture + ".10.0...22000\n" +
            $"[{section}]\n" +
            string.Join("\n", unrelated) +
            "\n%Target%=Install," + target + "\n";
    }

    private static DriverInfo Device(string hardwareId, string version,
        IReadOnlyList<string>? compatibleIds = null) =>
        new("PRIVATE_INSTANCE_01", "Generic Microsoft Device", "Microsoft",
            "Display", "Microsoft", version, null, "oem12.inf", true, true,
            0, [hardwareId], compatibleIds ?? []);

    private static MemoryStream Zip(params (string Path, string Inf)[] files)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var item = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(item.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        memory.Position = 0;
        return memory;
    }

    [Fact]
    public void GeneralPreviewRemainsCappedButFullTargetScanFindsNvidiaAtLine621()
    {
        var inf = LargeInf(NvidiaGpu);
        var preview = InfModelEvidence.ReadDeclaredIds(inf);
        Assert.Equal(500, preview.Count);
        Assert.DoesNotContain(preview, id => id.HardwareId == NvidiaGpu);

        var found = InfModelEvidence.ReadMatchingIds(
            inf, [Device(NvidiaGpu, "31.0.15.6000")]);
        var match = Assert.Single(found);
        Assert.Equal(NvidiaGpu, match.HardwareId);
        Assert.Equal("Models.NTamd64.10.0...22000", match.Section);
    }

    [Theory]
    [InlineData(NvidiaGpu, "31.0.15.6000")]
    [InlineData(KillerWifi, "23.0.1.1")]
    public void ZipOemMatchingDoesNotLoseDeviceAfterPreviewLimit(
        string target, string installedVersion)
    {
        using var archive = Zip(("official/driver.inf", LargeInf(target)));
        var installed = Device(target, installedVersion);

        var inventory = OemZipInfEvidence.Inspect(archive);
        Assert.Single(inventory.InfFiles);
        Assert.Equal(500, inventory.DeclaredIdCount);
        Assert.Contains(inventory.Warnings, note => note.Contains("500 ID"));

        archive.Position = 0;
        var report = LocalOemDriverComparison.Compare(
            archive, [installed], "x64", 10, 0, 26100);
        var matched = Assert.Single(report.Findings);
        Assert.Equal(2, matched.HardwareMatchStrength);
        Assert.True(matched.HasExactHardwareId);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            matched.PlatformVerdict);
        Assert.True(matched.IsPotentialNewer);
        Assert.Equal(1, report.PotentialNewerDevices);
        Assert.Contains(report.Notes, note => note.Contains("500 ID"));
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE_INSTANCE_01", report.Summary);
        Assert.DoesNotContain(target, report.Summary);
    }

    [Fact]
    public void PreflightUsesCompleteInfNotFirst500ModelIdentifiers()
    {
        var device = Device(NvidiaGpu, "31.0.15.6000");
        var preflight = InfPlatformPreflight.Assess(
            LargeInf(NvidiaGpu), device, "AMD64", 10, 0, 26100,
            catalogFileExists: false);
        Assert.Equal(2, preflight.HardwareMatchStrength);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            preflight.Result);
    }

    [Fact]
    public void TargetAfter500DoesNotBypassWrongArchitecture()
    {
        var device = Device(NvidiaGpu, "31.0.15.6000");
        var preflight = InfPlatformPreflight.Assess(
            LargeInf(NvidiaGpu, "NTarm64"), device,
            "x64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ArchitectureMismatch,
            preflight.Result);

        using var archive = Zip(("gpu.inf", LargeInf(NvidiaGpu, "NTarm64")));
        var report = LocalOemDriverComparison.Compare(
            archive, [device], "x64", 10, 0, 26100);
        Assert.Single(report.Findings);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void CompatibleIdAfter500RemainsWeakEvidenceNotInstallability()
    {
        var device = Device(
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_00000000",
            "31.0.15.6000", [NvidiaGpu]);
        using var archive = Zip(("gpu.inf", LargeInf(NvidiaGpu)));
        var report = LocalOemDriverComparison.Compare(
            archive, [device], "AMD64", 10, 0, 26100);
        var found = Assert.Single(report.Findings);
        Assert.Equal(1, found.HardwareMatchStrength);
        Assert.False(found.HasExactHardwareId);
        Assert.False(found.IsPotentialNewer);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void SimilarHardwareIdAfter500NeverBecomesMatch()
    {
        var device = Device(@"PCI\VEN_10DE&DEV_28A1&SUBSYS_16A217AA",
            "31.0.15.6000");
        var matching = InfModelEvidence.ReadMatchingIds(
            LargeInf(NvidiaGpu), [device]);
        Assert.Empty(matching);

        using var archive = Zip(("gpu.inf", LargeInf(NvidiaGpu)));
        var report = LocalOemDriverComparison.Compare(
            archive, [device], "x64", 10, 0, 26100);
        Assert.Empty(report.Findings);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void ExtractedOemFolderDoesNotMissModelsAfterPreviewLimit()
    {
        var folder = Directory.CreateTempSubdirectory("OmegaDrive-LargeInf-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "nvidia.inf"),
                LargeInf(NvidiaGpu), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder.FullName, "killer.inf"),
                LargeInf(KillerWifi), new UTF8Encoding(false));
            var result = LocalOemFolderComparison.Compare(
                folder.FullName,
                [
                    Device(NvidiaGpu, "31.0.15.6000"),
                    Device(KillerWifi, "23.0.1.1") with
                    {
                        DeviceId = "PRIVATE_INSTANCE_02",
                        Name = "Microsoft Network Controller",
                        DeviceClass = "Net"
                    }
                ], "x64", 10, 0, 26100);

            Assert.Equal(2, result.InfExamined);
            Assert.Equal(2, result.Findings.Count);
            Assert.Equal(2, result.PotentialNewerDevices);
            Assert.Contains(result.Notes, note => note.Contains("500 ID"));
            Assert.False(result.IsInstallable);
            Assert.False(result.IsVerifiedCompatible);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void HundredsOfUnrelatedIdsCannotOvercomeTargetValidation()
    {
        const string exact = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
        var malformed = LargeInf(exact).Replace(
            "%Target%=Install," + exact,
            @"%Target%=Install,PCI\VEN_10DE&DEV_28A0/../danger");

        Assert.Empty(InfModelEvidence.ReadMatchingIds(
            malformed, [Device(exact, "31.0.0.0")]));
    }

    [Fact]
    public void LimitOfInstalledDevicesAndInfSizeStillFailClosed()
    {
        var largeSet = Enumerable.Range(0, 2049)
            .Select(i => Device(NvidiaGpu, "31.0.0.0") with
            { DeviceId = "INSTANCE_" + i }).ToArray();

        Assert.Throws<InvalidDataException>(() =>
            InfModelEvidence.ReadMatchingIds(LargeInf(NvidiaGpu), largeSet));
        Assert.Throws<InvalidDataException>(() =>
            InfModelEvidence.ReadMatchingIds(new string('A', 1024 * 1024 + 1),
                [Device(NvidiaGpu, "31.0.0.0")]));
    }
}
