using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// One corrupted or over-budget INF never hides correctly parsed drivers
/// from the other INF files. Archive-level safety failures still stop scan.
/// </summary>
public sealed class OemMalformedInfRecoveryTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_11118086";
    private const string Broken = """
        [Version]
        Signature="$Windows NT$"
        DriverVer=10/04/2026,32.0.15.7000
        [Manufacturer]
        Vendor=Models,NTamd64
        [Models.NTamd64]
        Device=Install,PCI\VEN_10DE&DEV_28A0\
        """;

    private static string GoodInf(string id) => $"""
        [Version]
        Signature="$Windows NT$"
        DriverVer=10/04/2026,32.0.15.7000
        [Manufacturer]
        Vendor=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        Device=Install,{id}
        """;

    private static DriverInfo Device(string name, string hardwareId,
        string version = "31.0.15.6000") =>
        new("PRIVATE_INSTANCE_" + name, name, "Microsoft", "Display",
            "Microsoft", version, null, "oem9.inf", true, true, 0,
            [hardwareId], []);

    private static MemoryStream Zip(params (string Name, string Text)[] files)
    {
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(),
                    new UTF8Encoding(false));
                writer.Write(text);
            }
        }
        output.Position = 0;
        return output;
    }

    [Fact]
    public void MalformedInfInZipCannotHideValidNvidiaAndKillerPackages()
    {
        using var archive = Zip(
            ("a-broken/driver.inf", Broken),
            ("b-gpu/nvidia.inf", GoodInf(Nvidia)),
            ("c-wifi/killer.inf", GoodInf(Killer)));
        var devices = new[]
        {
            Device("NVIDIA laptop GPU", Nvidia),
            Device("Intel Killer Wi-Fi", Killer, "23.0.0.0")
        };

        var preview = OemZipInfEvidence.Inspect(archive);
        Assert.Equal(3, preview.InfFiles.Count);
        Assert.Equal(1, preview.RejectedInfCount);
        Assert.Equal(2, preview.InfWithDeclaredIds);
        Assert.Contains(preview.Warnings, note =>
            note.Contains("direttive malformate"));
        Assert.DoesNotContain("PRIVATE_INSTANCE", string.Join(" ", preview.Warnings));

        archive.Position = 0;
        var report = LocalOemDriverComparison.Compare(
            archive, devices, "x64", 10, 0, 26100);
        Assert.Equal(3, report.InfExamined);
        Assert.Equal(1, report.RejectedInfCount);
        Assert.Equal(2, report.InfWithDeclaredIds);
        Assert.Equal(2, report.Findings.Count);
        Assert.Equal(2, report.PotentialNewerDevices);
        Assert.Contains(report.Findings, row => row.DeviceName == "NVIDIA laptop GPU");
        Assert.Contains(report.Findings, row => row.DeviceName == "Intel Killer Wi-Fi");
        Assert.Contains(report.Notes, note => note.Contains("parziale"));
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE_INSTANCE", report.Summary);
        Assert.DoesNotContain(Nvidia, report.Summary);
    }

    [Fact]
    public void OnlyMalformedInfIsIncompleteNotVerifiedZeroDriverOffers()
    {
        using var archive = Zip(("vendor/bad.inf", Broken));
        var report = LocalOemDriverComparison.Compare(archive,
            [Device("NVIDIA laptop GPU", Nvidia)], "x64", 10, 0, 26100);
        Assert.Equal(1, report.InfExamined);
        Assert.Equal(1, report.RejectedInfCount);
        Assert.Empty(report.Findings);
        Assert.Contains(report.Notes, note => note.Contains("non una prova di assenza"));
        Assert.False(report.IsVerifiedCompatible);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void ExtractedOemFolderIsolatesBrokenInfWithoutExecutingAnything()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-OemMalformed-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "bad.inf"), Broken);
            File.WriteAllText(Path.Combine(root.FullName, "good.inf"), GoodInf(Nvidia));

            var report = LocalOemFolderComparison.Compare(root.FullName,
                [Device("NVIDIA laptop GPU", Nvidia)],
                "x64", 10, 0, 26100);
            Assert.Equal(2, report.InfExamined);
            Assert.Equal(1, report.RejectedInfCount);
            Assert.Single(report.Findings);
            Assert.Contains(report.Notes, note => note.Contains("sola lettura"));
            Assert.False(report.IsInstallable);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    // ReadDeclaredIds has a bounded 500-ID preview; targeted scanning
    // materializes all PC-specific matching IDs, with a separate hard cap
    // of 4096 matches. Exceeding THAT cap must reject only this INF rather
    // than returning 4096 potentially misleading partial device matches.
    [Fact]
    public void TargetedMatchOverflowRejectsOnlyAffectedInf()
    {
        static string Id(int value) => $@"PCI\VEN_8086&DEV_{value:X4}";
        var relatedDevices = Enumerable.Range(0, 1400).Select(i =>
            Device("Intel " + i, Id(i * 3), "22.0.0.0") with
            {
                HardwareIds = [Id(i * 3), Id(i * 3 + 1), Id(i * 3 + 2)]
            }).ToArray();

        var models = Enumerable.Range(0, 4200)
            .Select(i => $"Device{i}=Install,{Id(i)}");
        var excessive = """
            [Version]
            Signature="$Windows NT$"
            [Manufacturer]
            Vendor=Models,NTamd64.10.0...22000
            [Models.NTamd64.10.0...22000]
            """ + "\n" + string.Join("\n", models);

        using var archive = Zip(
            ("a-excess/driver.inf", excessive),
            ("b-good/nvidia.inf", GoodInf(Nvidia)));
        var devices = relatedDevices.Append(Device("NVIDIA laptop GPU", Nvidia))
            .ToArray();

        var preview = OemZipInfEvidence.Inspect(archive);
        Assert.Equal(0, preview.RejectedInfCount);
        Assert.Equal(501, preview.DeclaredIdCount);

        archive.Position = 0;
        var report = LocalOemDriverComparison.Compare(
            archive, devices, "x64", 10, 0, 26100);
        Assert.Equal(2, report.InfExamined);
        Assert.Equal(1, report.RejectedInfCount);
        Assert.Equal(1, report.InfWithDeclaredIds);
        var finding = Assert.Single(report.Findings);
        Assert.Equal("NVIDIA laptop GPU", finding.DeviceName);
        Assert.True(finding.IsPotentialNewer);
        Assert.Contains(report.Notes, note => note.Contains("confronto mirato"));
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
    }

    [Fact]
    public void MalformedInfDoesNotWeakenArchivePathTraversalRejection()
    {
        using var archive = Zip(
            ("../outside/private.inf", Broken),
            ("valid/driver.inf", GoodInf(Nvidia)));
        Assert.Throws<InvalidDataException>(() =>
            LocalOemDriverComparison.Compare(archive,
                [Device("NVIDIA laptop GPU", Nvidia)], "x64", 10, 0, 26100));
    }
}
