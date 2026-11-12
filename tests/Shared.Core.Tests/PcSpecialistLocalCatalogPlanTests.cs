using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
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
        string hardware, string type, string installedVersion = "30.0.0.0") =>
        new(instance, name, "Windows", type, "Windows",
            installedVersion, null, "oem-old.inf", true, true, 0,
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
            Assert.Contains("Nessun INF, ZIP, CAB o EXE", error.Message);
            Assert.Contains("non vengono eseguiti", error.Message);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void TwentyFourSourcesAllowedButTwentyFifthInfFolderFailsClosed()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-24-");
        try
        {
            for (var i = 0; i < PcSpecialistLocalCatalogPlan.MaximumSources; i++)
                File.WriteAllBytes(Path.Combine(root.FullName,
                    $"driver-{i:D2}.zip"), [0x50, 0x4B, 0x03, 0x04]);

            var plan = PcSpecialistLocalCatalogPlan.Create(root.FullName);
            Assert.Equal(24, plan.SourceCount);
            Assert.False(plan.HasLooseInf);

            File.WriteAllText(Path.Combine(root.FullName, "driver.inf"),
                Inf(Killer, "24.1.1.0"));
            var failure = Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(root.FullName));
            Assert.Contains("Oltre 24 sorgenti", failure.Message);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }


    [Fact]
    public void FullTwentyFourZipCatalogFindsLateNvidiaAndKillerWithoutCountingRepeatedRealtekAsUpdates()
    {
        // Real on-disk ZIPs, not fabricated report objects. A notebook
        // bundle with audio packages first cannot lose the last GPU/NIC.
        var root = Directory.CreateTempSubdirectory("OmegaDrive-Recoil24-");
        try
        {
            for (var i = 0; i < 22; i++)
                WriteZip(root.FullName, $"audio-{i:D2}.zip", Realtek);
            WriteZip(root.FullName, "z-gpu.zip", Nvidia);
            WriteZip(root.FullName, "z-killer.zip", Killer);
            var plan = PcSpecialistLocalCatalogPlan.Create(root.FullName);

            Assert.Equal(PcSpecialistLocalCatalogPlan.MaximumSources, plan.SourceCount);
            Assert.False(plan.IsPartial);
            Assert.Equal(24, plan.Archives.Count);
            var inventory = new[]
            {
                Device("PRIVATE_AUDIO", "Realtek Audio", Realtek, "MEDIA"),
                Device("PRIVATE_GPU", "NVIDIA RTX 4060", Nvidia, "Display"),
                Device("PRIVATE_KILLER", "Killer Wi-Fi", Killer, "Net")
            };
            var results = new List<LocalOemDriverComparison.Report>();
            foreach (var archive in plan.Archives)
            {
                using var input = File.OpenRead(archive);
                var result = LocalOemDriverComparison.Compare(
                    input, inventory, "AMD64", 10, 0, 26100);
                Assert.False(result.IsInstallable);
                results.Add(result);
            }

            var combined = LocalOemArchiveBatchSummary.Combine(results);
            Assert.Equal(24, combined.ArchivesExamined);
            Assert.Equal(24, combined.TotalAssociations);
            Assert.Equal(3, combined.DistinctMatchedDevices);
            Assert.Equal(21, combined.RepeatedAssociations);
            Assert.Equal(3, combined.Findings.Count);
            Assert.Contains(combined.Findings, row =>
                row.DeviceName == "NVIDIA RTX 4060" &&
                row.HasExactHardwareId && row.IsPotentialNewer);
            Assert.Contains(combined.Findings, row =>
                row.DeviceName == "Killer Wi-Fi" &&
                row.HasExactHardwareId && row.IsPotentialNewer);
            Assert.DoesNotContain("PRIVATE_ORDER", combined.Summary);
            Assert.DoesNotContain("PRIVATE_INSTANCE", combined.Summary);
            Assert.False(combined.IsInstallable);
            Assert.False(combined.IsVerifiedCompatible);

            // The same directory becomes over budget with one extra
            // loose INF. Never silently omit that 25th source.
            File.WriteAllText(Path.Combine(root.FullName, "extra.inf"),
                Inf(Nvidia, "32.1.0.0"));
            var ex = Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(root.FullName));
            Assert.Contains("Oltre 24 sorgenti", ex.Message);
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

    // Real PE32+ skeleton, one section [0x200, 0x400). The genuine ZIP
    // begins after the PE section: these bytes are NEVER executed.
    private static byte[] PeStub()
    {
        var bytes = new byte[0x400];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3C), 0x80);
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x84), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x86), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x94), 0xF0);
        const int table = 0x80 + 24 + 0xF0;
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(table + 16), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(table + 20), 0x200);
        return bytes;
    }

    private static string WritePeZip(string folder, string name, string id)
    {
        var path = Path.Combine(folder, name);
        using var zipBytes = new MemoryStream();
        using (var zip = new ZipArchive(
            zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("PRIVATE_ORDER_8844/driver.inf");
            using var writer = new StreamWriter(
                entry.Open(), new UTF8Encoding(false));
            writer.Write(Inf(id, "32.0.16.9500"));
        }
        using (var destination = File.Create(path))
        {
            destination.Write(PeStub());
            zipBytes.Position = 0;
            zipBytes.CopyTo(destination);
        }
        return path;
    }

    [Theory]
    [InlineData(true, true, 3, true)]
    [InlineData(true, true, 0, false)]
    [InlineData(true, false, 3, false)]
    [InlineData(false, true, 3, false)]
    [InlineData(true, true, 2049, false)]
    public void GenericOemImportDependsOnVerifiedPnpNotSmbiosManufacturer(
        bool scanned, bool inventory, int devices, bool expected)
    {
        Assert.Equal(expected,
            PcSpecialistLocalCatalogPlan.CanOfferImport(
                scanned, inventory, devices));
        // A Clevo/TongFang/Notebook machine can now use the same local
        // reader even when SystemOemCatalog.Match returns no OEM.
        Assert.Null(SystemOemCatalog.Match(
            "Notebook", "Generic Laptop"));
    }

    [Fact]
    public void TruePeZipExeInMixedOemBundleYieldsNvidiaKillerAndRealtek()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-PE-ZIP-");
        try
        {
            var nvidiaExe = WritePeZip(root.FullName,
                "PRIVATE_ORDER_8844-GPU.exe", Nvidia);
            WriteZip(root.FullName, "network.zip", Killer);
            File.WriteAllText(Path.Combine(root.FullName, "audio.inf"),
                Inf(Realtek, "6.0.9700.0"));
            File.WriteAllBytes(Path.Combine(root.FullName, "legacy.7z"),
                [0x37, 0x7A, 0xBC, 0xAF]);

            var plan = PcSpecialistLocalCatalogPlan.Create(root.FullName);
            Assert.Equal(2, plan.Archives.Count);
            Assert.Equal(3, plan.SourceCount);
            Assert.Equal(1, plan.UnsupportedPackages);
            Assert.True(plan.HasLooseInf);
            Assert.True(plan.IsPartial);
            Assert.Contains(nvidiaExe, plan.Archives);

            var devices = new[]
            {
                Device("PRIVATE_GPU", "NVIDIA RTX 4060", Nvidia, "Display"),
                Device("PRIVATE_WIFI", "Killer Wi-Fi", Killer, "Net"),
                Device("PRIVATE_AUDIO", "Realtek Audio", Realtek,
                    "MEDIA", installedVersion: "5.0.0.0")
            };
            var evidence = new List<LocalOemDriverComparison.Report>
            {
                LocalOemFolderComparison.Compare(root.FullName,
                    devices, "AMD64", 10, 0, 26100)
            };
            foreach (var archive in plan.Archives)
            {
                using var stream = File.OpenRead(archive);
                evidence.Add(archive.EndsWith(".exe",
                    StringComparison.OrdinalIgnoreCase)
                    ? LocalOemPeZipComparison.Compare(stream, devices,
                        "AMD64", 10, 0, 26100)
                    : LocalOemDriverComparison.Compare(stream, devices,
                        "AMD64", 10, 0, 26100));
            }
            var batch = LocalOemArchiveBatchSummary.Combine(evidence);
            Assert.Equal(3, batch.DistinctMatchedDevices);
            Assert.Equal(3, batch.Findings.Count);
            Assert.Equal(3, batch.PotentialNewerShown);
            Assert.All(batch.Findings, finding =>
                Assert.True(finding.HasExactHardwareId));
            Assert.Contains(batch.Findings, f =>
                f.DeviceName.Contains("NVIDIA"));
            Assert.Contains(batch.Findings, f =>
                f.DeviceName.Contains("Killer"));
            Assert.Contains(batch.Findings, f =>
                f.DeviceName.Contains("Realtek"));
            Assert.False(batch.IsInstallable);
            Assert.DoesNotContain("PRIVATE_ORDER", batch.Summary);
            Assert.DoesNotContain("8844", batch.Summary);
            Assert.All(batch.Findings, finding =>
                Assert.DoesNotContain("PRIVATE_ORDER", finding.InfName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ProprietaryExeWithoutZipIsCountedNotExecutedOrConsideredUpdate()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-Proprietary-");
        try
        {
            var exe = Path.Combine(root.FullName, "setup.exe");
            File.WriteAllBytes(exe, PeStub()); // Valid PE, no appended ZIP
            WriteZip(root.FullName, "wifi.zip", Killer);

            var plan = PcSpecialistLocalCatalogPlan.Create(root.FullName);
            Assert.Single(plan.Archives);
            Assert.Equal(1, plan.SourceCount);
            Assert.Equal(1, plan.UnsupportedPackages);
            Assert.True(plan.IsPartial);
            Assert.False(plan.IsInstallable);
            Assert.False(plan.IsVerifiedOfficialSource);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void MalformedZipAppendedToPeAbortsWholeFolderReview()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-UnsafePE-");
        try
        {
            var exe = Path.Combine(root.FullName, "unsafe.exe");
            using (var file = File.Create(exe))
            {
                file.Write(PeStub());
                file.Write([0x50, 0x4B, 0x03, 0x04]);
                file.Write(new byte[70]);
            }
            WriteZip(root.FullName, "wifi.zip", Killer);
            Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void UntrustedPeZipExeCountsTowardsTwentyFourSourceCap()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-BudgetPE-");
        try
        {
            for (var i = 0; i < PcSpecialistLocalCatalogPlan.MaximumSources; i++)
                WritePeZip(root.FullName, $"driver-{i:D2}.exe", Nvidia);
            Assert.Equal(24,
                PcSpecialistLocalCatalogPlan.Create(root.FullName).SourceCount);
            File.WriteAllText(Path.Combine(root.FullName, "extra.inf"),
                Inf(Killer, "24.1.0.0"));
            Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void TinyExeInstallersDoNotExhaustPeZipProbeBudget()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-TinyExe-");
        try
        {
            // Created before the real driver, these are provably too small
            // to contain a valid PE header or ZIP overlay. They still
            // count as unsupported, but must not consume any of 24 probes.
            for (var i = 0; i < PcSpecialistLocalCatalogPlan.MaximumExeProbes; i++)
                File.WriteAllBytes(Path.Combine(root.FullName,
                    $"setup-{i:D2}.exe"), [0x4D, 0x5A, 0, 0]);

            var driver = WritePeZip(root.FullName, "z-nvidia.exe", Nvidia);
            var plan = PcSpecialistLocalCatalogPlan.Create(root.FullName);

            Assert.Single(plan.Archives);
            Assert.Equal(driver, plan.Archives[0]);
            Assert.Equal(1, plan.SourceCount);
            Assert.Equal(PcSpecialistLocalCatalogPlan.MaximumExeProbes,
                plan.UnsupportedPackages);
            Assert.True(plan.IsPartial);
            Assert.Equal(PcSpecialistLocalCatalogPlan.MaximumExeProbes + 1,
                plan.FilesVisited);
            Assert.False(plan.IsInstallable);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ProbeChoiceIsStableWhenMoreThanTwentyFourEligibleExesExist()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-StableExe-");
        try
        {
            // The valuable PE-ZIP is created last, but its ordinal name
            // sorts first; NTFS enumeration order must not decide whether
            // the valid NVIDIA package is included in the 24 probes.
            for (var i = 0; i < PcSpecialistLocalCatalogPlan.MaximumExeProbes; i++)
                File.WriteAllBytes(Path.Combine(root.FullName,
                    $"setup-{i:D2}.exe"), PeStub());
            var driver = WritePeZip(root.FullName, "00-nvidia.exe", Nvidia);

            var first = PcSpecialistLocalCatalogPlan.Create(root.FullName);
            var second = PcSpecialistLocalCatalogPlan.Create(root.FullName);

            Assert.Equal(new[] { driver }, first.Archives);
            Assert.Equal(first.Archives, second.Archives);
            Assert.Equal(PcSpecialistLocalCatalogPlan.MaximumExeProbes,
                first.UnsupportedPackages);
            Assert.True(first.IsPartial);
            Assert.Equal(25, first.FilesVisited);
            Assert.False(first.IsVerifiedOfficialSource);
        }
        finally
        {
            root.Delete(recursive: true);
        }
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

    [Fact]
    public void UnsupportedOnlyFolderRequiresSavedHtmlModeAndReconcilesWithoutInfEvidence()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-PRIVATE_ORDER_318-");
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "W11 Nvidia VGA.exe"),
                [0x4D, 0x5A, 0, 0]);
            File.WriteAllBytes(Path.Combine(root.FullName, "W11 Control Centre.msi"),
                [0x4D, 0x53, 0x49, 0]);
            File.WriteAllBytes(Path.Combine(root.FullName, "W11 Firmware.7z"),
                [0x37, 0x7A, 0xBC, 0xAF]);

            // Without an explicitly saved HTML catalog, the old gate remains.
            Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(root.FullName));

            var plan = PcSpecialistLocalCatalogPlan.Create(root.FullName,
                allowUnsupportedOnly: true);
            Assert.Empty(plan.Archives);
            Assert.False(plan.HasLooseInf);
            Assert.Equal(0, plan.SourceCount);
            Assert.Equal(3, plan.UnsupportedPackages);
            Assert.True(plan.IsPartial);
            Assert.False(plan.IsInstallable);

            var snapshot = new[]
            {
                new PcSpecialistPortalSnapshot.Package(
                    "W11 Nvidia VGA.exe", "NVIDIA", "Solo lettura"),
                new PcSpecialistPortalSnapshot.Package(
                    "W11 Control Centre.msi", "Software", "Solo lettura"),
                new PcSpecialistPortalSnapshot.Package(
                    "W11 Intel WiFi.zip", "Wi-Fi", "Solo lettura")
            };
            var report = PcSpecialistPackageReconciliation.Compare(
                snapshot, plan.UnsupportedPackageNames.Select(
                    PcSpecialistPackageReconciliation.LocalSource.Unsupported)
                    .ToArray());
            Assert.Equal(2, report.Unsupported);
            Assert.Equal(1, report.NotObserved);
            Assert.Equal(0, report.Examined);
            Assert.Equal(0, report.WithPnPEvidence);
            Assert.False(report.IsComplete);
            Assert.False(report.IsInstallable);
            Assert.DoesNotContain("PRIVATE_ORDER", report.Summary);
            Assert.DoesNotContain(root.FullName, report.Summary);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void UnsupportedOnlyModeStillRejectsEmptyOrUnrelatedFolders()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-EmptyOEM-");
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(root.FullName,
                    allowUnsupportedOnly: true));
            File.WriteAllText(Path.Combine(root.FullName, "readme.txt"),
                "Nothing inspectable.");
            Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(root.FullName,
                    allowUnsupportedOnly: true));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void DuplicateUnsupportedInstallersRemainAmbiguousWhenNoArchivesExist()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-DuplicateOEM-");
        try
        {
            var child = Directory.CreateDirectory(Path.Combine(
                root.FullName, "nested"));
            File.WriteAllBytes(Path.Combine(root.FullName, "W11 Wifi.exe"),
                [0x4D, 0x5A, 0, 0]);
            File.WriteAllBytes(Path.Combine(child.FullName, "W11 Wifi.exe"),
                [0x4D, 0x5A, 0, 0]);
            var plan = PcSpecialistLocalCatalogPlan.Create(root.FullName,
                allowUnsupportedOnly: true);
            var report = PcSpecialistPackageReconciliation.Compare(
                [new PcSpecialistPortalSnapshot.Package(
                    "W11 Wifi.exe", "Wi-Fi", "Solo lettura")],
                plan.UnsupportedPackageNames.Select(
                    PcSpecialistPackageReconciliation.LocalSource.Unsupported)
                    .ToArray());
            Assert.Equal(1, report.Ambiguous);
            Assert.Equal(0, report.Unsupported);
            Assert.Equal(0, report.WithPnPEvidence);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }


    [Fact]
    public void ProbeBudgetUsesGlobalBasenameOrderAcrossSubfolders()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-GlobalProbe-");
        try
        {
            // DFS pushes a-first then z-last, and thus used to burn all 24
            // probes on z-last before discovering an NVIDIA PE-ZIP in a-first.
            var gpuFolder = Directory.CreateDirectory(Path.Combine(
                root.FullName, "a-first"));
            var setupFolder = Directory.CreateDirectory(Path.Combine(
                root.FullName, "z-last"));
            var nvidia = WritePeZip(gpuFolder.FullName,
                "00-Nvidia-VGA.exe", Nvidia);
            for (var i = 0; i < PcSpecialistLocalCatalogPlan.MaximumExeProbes; i++)
                File.WriteAllBytes(Path.Combine(setupFolder.FullName,
                    $"setup-{i:D2}.exe"), PeStub());

            var plan = PcSpecialistLocalCatalogPlan.Create(root.FullName);
            Assert.Equal(new[] { nvidia }, plan.Archives);
            Assert.Equal(24, plan.UnsupportedPackages);
            Assert.True(plan.IsPartial);
            Assert.Equal(25, plan.FilesVisited);
            Assert.False(plan.IsInstallable);
            Assert.False(plan.IsVerifiedOfficialSource);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void SavedHtmlExactNamePrioritizesLateNvidiaWithoutFuzzyMatches()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-HTMLProbe-");
        try
        {
            var killer = WritePeZip(root.FullName, "setup-00.exe", Killer);
            for (var i = 1; i < PcSpecialistLocalCatalogPlan.MaximumExeProbes; i++)
                File.WriteAllBytes(Path.Combine(root.FullName,
                    $"setup-{i:D2}.exe"), PeStub());
            var nvidia = WritePeZip(root.FullName,
                "z-Nvidia-VGA.exe", Nvidia);

            // Without the saved HTML, bounded global lexical order leaves
            // NVIDIA uninspected; with exact filename, NVIDIA is inspected.
            var regular = PcSpecialistLocalCatalogPlan.Create(root.FullName);
            Assert.Equal(new[] { killer }, regular.Archives);
            Assert.Equal(24, regular.UnsupportedPackages);

            var prioritized = PcSpecialistLocalCatalogPlan.Create(
                root.FullName, preferredPackageNames: ["z-Nvidia-VGA.exe"]);
            Assert.Equal(new[] { killer, nvidia }, prioritized.Archives);
            Assert.Equal(23, prioritized.UnsupportedPackages);
            Assert.Equal(25, prioritized.FilesVisited);
            Assert.Contains("setup-23.exe", prioritized.UnsupportedPackageNames);
            Assert.True(prioritized.IsPartial);
            Assert.False(prioritized.IsInstallable);
            Assert.False(prioritized.IsVerifiedOfficialSource);
            Assert.DoesNotContain(root.FullName, string.Join(" ",
                prioritized.UnsupportedPackageNames));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void UntrustedHtmlPriorityNamesNeverOverrideReadOnlyBudget()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-UnsafePriority-");
        try
        {
            WritePeZip(root.FullName, "Nvidia-VGA.exe", Nvidia);
            Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(root.FullName,
                    preferredPackageNames: [@"C:\\PRIVATE_ORDER\\Nvidia-VGA.exe"]));
            Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(root.FullName,
                    preferredPackageNames: ["Nvidia-VGA.exe", "nvidia-vga.EXE"]));
            Assert.Throws<InvalidDataException>(() =>
                PcSpecialistLocalCatalogPlan.Create(root.FullName,
                    preferredPackageNames: Enumerable.Range(0,
                        PcSpecialistPortalSnapshot.MaximumLinks + 1)
                        .Select(i => $"W11 Setup {i:D3}.exe").ToArray()));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }


    [Fact]
    public void FullHtmlCatalogFairlyProbesRealLateNvidiaAndKillerPeZip()
    {
        // 130 chipset and 130 NVIDIA entries: the actual RTX EXE is
        // outside the initial 96-row HTML preview. Twenty-five local
        // chipset EXEs formerly consumed all 24 PE-ZIP probes first.
        var root = Directory.CreateTempSubdirectory("OmegaDrive-FairPeZip-");
        try
        {
            var intelNames = Enumerable.Range(0, 130)
                .Select(i => $"W11 Intel Chipset {i:D3}.exe").ToArray();
            var nvidiaNames = Enumerable.Range(0, 130)
                .Select(i => $"W11 Nvidia VGA {i:D3}.exe").ToArray();
            const string killerName = "W11 Killer WiFi 2026.exe";
            var html = "<html>Download Drivers / Manuals " +
                "Date Uploaded File Size" +
                string.Concat(intelNames.Concat(nvidiaNames)
                    .Append(killerName).Select(name =>
                        "<a href=\"https://www.pcspecialist.co.uk/downloads/driver.zip\">" +
                        name + "</a>")) + "</html>";
            var snapshot = PcSpecialistPortalSnapshot.Parse(html);
            Assert.Equal(261, snapshot.TotalDistinct);
            Assert.Equal(261, snapshot.AllParsedPackages.Count);
            Assert.Equal(96, snapshot.Packages.Count);
            Assert.DoesNotContain(snapshot.Packages,
                row => row.Name == nvidiaNames[^1]);

            for (var i = 0; i < 25; i++)
                File.WriteAllBytes(Path.Combine(root.FullName, intelNames[i]),
                    PeStub()); // real PE bytes, no ZIP: never executed
            var gpu = WritePeZip(root.FullName, nvidiaNames[^1], Nvidia);
            var wifi = WritePeZip(root.FullName, killerName, Killer);

            var oldPreview = PcSpecialistLocalCatalogPlan.Create(
                root.FullName, allowUnsupportedOnly: true,
                preferredPackageNames:
                    snapshot.Packages.Select(row => row.Name).ToArray());
            // With only the old 96-row HTML preview, the new category
            // fairness does already protect the unique Killer entry.
            // The NVIDIA package at index 129 is STILL invisible because
            // its exact name was omitted from the initial preview.
            Assert.DoesNotContain(gpu, oldPreview.Archives);
            Assert.Contains(wifi, oldPreview.Archives);
            Assert.True(oldPreview.IsPartial);

            var plan = PcSpecialistLocalCatalogPlan.Create(root.FullName,
                preferredPackageNames:
                    snapshot.AllParsedPackages.Select(row => row.Name).ToArray());
            Assert.Equal(2, plan.Archives.Count);
            Assert.Contains(gpu, plan.Archives);
            Assert.Contains(wifi, plan.Archives);
            Assert.Equal(27, plan.FilesVisited);
            Assert.Equal(25, plan.UnsupportedPackages);
            Assert.True(plan.IsPartial); // not all 27 EXEs inspected
            Assert.False(plan.IsInstallable);
            Assert.False(plan.IsVerifiedOfficialSource);

            var inventory = new[] {
                Device("PRIVATE_GPU_SERIAL", "Generic display", Nvidia, "Display"),
                Device("PRIVATE_WIFI_SERIAL", "Generic network", Killer, "Net")
            };
            var evidence = new List<LocalOemDriverComparison.Report>();
            foreach (var archive in plan.Archives)
            {
                using var input = File.OpenRead(archive);
                var report = LocalOemPeZipComparison.Compare(
                    input, inventory, "AMD64", 10, 0, 26100);
                Assert.False(report.IsInstallable);
                evidence.Add(report);
            }
            var combined = LocalOemArchiveBatchSummary.Combine(evidence);
            Assert.Equal(2, combined.DistinctMatchedDevices);
            Assert.Equal(2, combined.Findings.Count);
            Assert.All(combined.Findings,
                finding => Assert.True(finding.HasExactHardwareId));
            Assert.False(combined.IsInstallable);
            Assert.False(combined.IsVerifiedCompatible);
            Assert.DoesNotContain("PRIVATE_GPU_SERIAL", combined.Summary);
            Assert.DoesNotContain("PRIVATE_WIFI_SERIAL", combined.Summary);

            var visible = PcSpecialistPortalSnapshot.ChoosePreviewForLocal(
                snapshot.AllParsedPackages,
                plan.Archives.Select(archive => Path.GetFileName(archive)!));
            Assert.Contains(visible, row => row.Name == nvidiaNames[^1]);
            Assert.Contains(visible, row => row.Name == killerName);
            Assert.Equal(96, visible.Count);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void LongHtmlPriorityListMatchesOnlyExactSafeBasenames()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-FairPeZipName-");
        try
        {
            const string name = "W11 Nvidia VGA 120.exe";
            var gpu = WritePeZip(root.FullName, name, Nvidia);
            for (var i = 0; i < PcSpecialistLocalCatalogPlan.MaximumExeProbes; i++)
                File.WriteAllBytes(Path.Combine(root.FullName,
                    $"W11 Intel Chipset {i:D3}.exe"), PeStub());
            var favorites = Enumerable.Range(0, 110)
                .Select(i => $"W11 Intel Chipset {i:D3}.exe")
                .Append("W11 Nvidia VGA 120 new.exe").ToArray();

            var noExact = PcSpecialistLocalCatalogPlan.Create(
                root.FullName, allowUnsupportedOnly: true,
                preferredPackageNames: favorites);
            Assert.DoesNotContain(gpu, noExact.Archives);
            var exact = PcSpecialistLocalCatalogPlan.Create(root.FullName,
                preferredPackageNames: favorites.Append(name).ToArray());
            Assert.Equal(gpu, Assert.Single(exact.Archives));
            Assert.Equal(PcSpecialistLocalCatalogPlan.MaximumExeProbes,
                exact.UnsupportedPackages);
            Assert.True(exact.IsPartial);
            Assert.False(exact.IsInstallable);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
