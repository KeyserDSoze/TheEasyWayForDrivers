using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class PcSpecialistLocalCatalogPlanTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static string Inf(string id, string version) => $"""
        [Version]
        Signature="$Windows NT$"
        DriverVer=11/08/2026,{version}
        [Manufacturer]
        Vendor=Models,NTamd64
        [Models.NTamd64]
        Model=Install,{id}
        """;

    private static DriverInfo Device(string instance, string name,
        string hardware, string type) =>
        new(instance, name, "Windows", type, "Windows",
            "30.0.0.0", null, "oem-old.inf", true, true, 0,
            [hardware], []);

    private static string WriteZip(string folder, string name, string id)
    {
        var path = Path.Combine(folder, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("order-PRIVATE/drivers/" +
            "PRIVATE_" + name + ".inf");
        using var writer = new StreamWriter(
            entry.Open(), new UTF8Encoding(false));
        writer.Write(Inf(id, "32.0.16.9500"));
        return path;
    }

    [Fact]
    public void MixedDownloadedFolderDiscoversZipCabAndExtractedInfWithoutExecution()
    {
        var folder = Directory.CreateTempSubdirectory("OmegaDrive-LocalOEM-");
        try
        {
            var child = Directory.CreateDirectory(Path.Combine(
                folder.FullName, "PRIVATE_ORDER_159753"));
            var zip = WriteZip(child.FullName, "video.ZIP", Nvidia);
            var cab = Path.Combine(child.FullName, "wireless.CAB");
            File.WriteAllBytes(cab, [0x4D, 0x53, 0x43, 0x46]);
            File.WriteAllText(Path.Combine(child.FullName, "audio.inf"),
                Inf(Realtek, "6.0.1.0"));
            File.WriteAllBytes(Path.Combine(child.FullName, "setup.exe"),
                [0x4D, 0x5A, 0x00, 0x00]);
            File.WriteAllBytes(Path.Combine(child.FullName, "recoil.msi"),
                [0x4D, 0x53, 0x49, 0x00]);
            File.WriteAllBytes(Path.Combine(child.FullName, "extra.7z"),
                [0x37, 0x7A, 0xBC, 0xAF]);
            File.WriteAllText(Path.Combine(child.FullName, "readme.txt"),
                "PRIVATE_ORDER_159753");

            var plan = PcSpecialistLocalCatalogPlan.Create(folder.FullName);
            Assert.Equal(2, plan.Archives.Count);
            Assert.Contains(zip, plan.Archives);
            Assert.Contains(cab, plan.Archives);
            Assert.True(plan.HasLooseInf);
            Assert.True(plan.IsPartial);
            Assert.Equal(3, plan.UnsupportedPackages);
            Assert.Equal(3, plan.SourceCount);
            Assert.False(plan.IsInstallable);
            Assert.False(plan.IsVerifiedOfficialSource);
            Assert.Equal(2, plan.DirectoriesVisited);
            Assert.Equal(7, plan.FilesVisited);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void OneLocalInfFolderPlusMultipleArchivesDeduplicatesPnpDevices()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-Bundle-");
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(
                root.FullName, "PRIVATE_ORDER_773311"));
            WriteZip(root.FullName, "nvidia.zip", Nvidia);
            WriteZip(root.FullName, "gpu-duplicate.zip", Nvidia);
            WriteZip(root.FullName, "wifi.zip", Killer);
            File.WriteAllText(Path.Combine(nested.FullName, "gpu.inf"),
                Inf(Nvidia, "32.0.18.0"));
            File.WriteAllText(Path.Combine(nested.FullName, "audio.inf"),
                Inf(Realtek, "6.0.1.0"));

            var devices = new[]
            {
                Device("PRIVATE_GPU_INSTANCE", "NVIDIA RTX 4060", Nvidia,
                    "Display"),
                Device("PRIVATE_KILLER_INSTANCE", "Killer Wi-Fi", Killer,
                    "Net"),
                Device("PRIVATE_REALTEK_INSTANCE", "Realtek Audio", Realtek,
                    "MEDIA")
            };
            var plan = PcSpecialistLocalCatalogPlan.Create(root.FullName);
            Assert.Equal(3, plan.Archives.Count);
            Assert.True(plan.HasLooseInf);
            Assert.Equal(4, plan.SourceCount);
            Assert.False(plan.IsPartial);

            var sources = new List<LocalOemDriverComparison.Report>
            {
                LocalOemFolderComparison.Compare(root.FullName,
                    devices, "AMD64", 10, 0, 26100)
            };
            foreach (var file in plan.Archives)
            {
                using var stream = File.OpenRead(file);
                sources.Add(LocalOemDriverComparison.Compare(
                    stream, devices, "AMD64", 10, 0, 26100));
            }
            var combined = LocalOemArchiveBatchSummary.Combine(sources);

            Assert.Equal(4, combined.ArchivesExamined);
            Assert.Equal(3, combined.DistinctMatchedDevices);
            Assert.True(combined.RepeatedAssociations > 0);
            Assert.Equal(3, combined.Findings.Count);
            Assert.Contains(combined.Findings, finding =>
                finding.DeviceName.Contains("NVIDIA") &&
                finding.HasExactHardwareId);
            Assert.Contains(combined.Findings, finding =>
                finding.DeviceName.Contains("Killer") &&
                finding.HasExactHardwareId);
            Assert.Contains(combined.Findings, finding =>
                finding.DeviceName.Contains("Realtek") &&
                finding.HasExactHardwareId);
            Assert.False(combined.IsInstallable);
            Assert.False(combined.IsVerifiedCompatible);
            Assert.DoesNotContain("PRIVATE_ORDER", combined.Summary);
            Assert.All(combined.Findings, finding =>
            {
                Assert.DoesNotContain("PRIVATE_ORDER", finding.InfName);
                Assert.DoesNotContain("PRIVATE_", finding.InfName);
            });
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void UnsupportedInstallersAloneCannotProveAbsenceOfOemDrivers()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-OnlyExe-");
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "official.exe"),
                [0x4D, 0x5A, 0, 0]);
            File.WriteAllBytes(Path.Combine(root.FullName, "extra.7z"),
                [0x37, 0x7A, 0, 0]);

            var error = Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(root.FullName));
            Assert.Contains("Nessun INF, ZIP o CAB", error.Message);
            Assert.Contains("non vengono eseguiti", error.Message);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void TwelveSourcesAllowedButThirteenthOrInfFolderFailsClosed()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-12-");
        try
        {
            for (var i = 0; i < 12; i++)
                File.WriteAllBytes(Path.Combine(root.FullName,
                    $"driver-{i:D2}.zip"), [0x50, 0x4B, 0x03, 0x04]);

            var plan = PcSpecialistLocalCatalogPlan.Create(root.FullName);
            Assert.Equal(12, plan.SourceCount);
            Assert.False(plan.HasLooseInf);

            File.WriteAllText(Path.Combine(root.FullName, "driver.inf"),
                Inf(Killer, "24.1.1.0"));
            var failure = Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(root.FullName));
            Assert.Contains("Oltre 12 sorgenti", failure.Message);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void EmptyMissingOrCancelledImportNeverClaimsNoDriversExist()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-Empty-");
        var path = root.FullName;
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(path));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                PcSpecialistLocalCatalogPlan.Create(path, cts.Token));
        }
        finally
        {
            root.Delete(recursive: true);
        }
        Assert.Throws<InvalidDataException>(() =>
            PcSpecialistLocalCatalogPlan.Create(path));
    }

    [Fact]
    public void UntrustedFileLinksAreRejectedWhenFileSystemSupportsThem()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-Link-");
        var outside = Directory.CreateTempSubdirectory("OmegaDrive-Outside-");
        try
        {
            var target = Path.Combine(outside.FullName, "nvidia.inf");
            File.WriteAllText(target, Inf(Nvidia, "32.0.0.0"));
            var link = Path.Combine(root.FullName, "linked.inf");
            try
            {
                File.CreateSymbolicLink(link, target);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException or
                PlatformNotSupportedException)
            {
                // Windows runners can disallow creating symlinks; the
                // read-only production link check is still compiled.
                return;
            }

            Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
            outside.Delete(recursive: true);
        }
    }
}
