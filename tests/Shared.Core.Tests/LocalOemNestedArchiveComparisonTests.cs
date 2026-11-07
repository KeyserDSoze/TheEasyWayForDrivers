using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class LocalOemNestedArchiveComparisonTests
{
    private const string NvidiaId = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string KillerId = @"PCI\VEN_8086&DEV_51F1";
    private const string RealtekId = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string NvidiaInf = """
        [Version]
        Signature="$Windows NT$"
        Class=Display
        DriverVer=10/04/2026,32.0.15.7000
        [Manufacturer]
        Vendor=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        NvidiaGpu=Install,PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA
        """;
    private const string NvidiaNewerInf = """
        [Version]
        Signature="$Windows NT$"
        Class=Display
        DriverVer=10/04/2026,33.0.0.1
        [Manufacturer]
        Vendor=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        NvidiaGpu=Install,PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA
        """;
    private const string KillerInf = """
        [Version]
        Signature="$Windows NT$"
        Class=Net
        DriverVer=10/04/2026,24.0.12.100
        [Manufacturer]
        Vendor=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        KillerWifi=Install,PCI\VEN_8086&DEV_51F1
        """;
    private const string RealtekInf = """
        [Version]
        Signature="$Windows NT$"
        Class=MEDIA
        DriverVer=10/04/2026,10.0.2.100
        [Manufacturer]
        Vendor=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        RealtekAudio=Install,HDAUDIO\FUNC_01&VEN_10EC&DEV_0295
        """;

    private static readonly DriverInfo[] Devices =
    [
        Device("GPU PRIVATE_INSTANCE", "Microsoft Basic Display Adapter",
            NvidiaId, "31.0.0.0"),
        Device("KILLER PRIVATE_INSTANCE", "Killer Wi-Fi",
            KillerId, "23.0.0.0"),
        Device("REALTEK PRIVATE_INSTANCE", "High Definition Audio Device",
            RealtekId, "9.0.0.0")
    ];

    private static DriverInfo Device(
        string instance, string name, string id, string version) =>
        new(instance, name, "Microsoft", "Display", "Microsoft",
            version, null, "oem2.inf", true, true, 0, [id], []);

    private static byte[] AsZip(
        params (string Name, byte[] Contents)[] files)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, contents) in files)
            {
                var entry = archive.CreateEntry(
                    name, CompressionLevel.Optimal);
                using var sink = entry.Open();
                sink.Write(contents);
            }
        }
        return buffer.ToArray();
    }

    private static (string Name, byte[] Contents) Inf(
        string path, string text) =>
        (path, Encoding.UTF8.GetBytes(text));

    private static (string Name, byte[] Contents) InnerZip(
        string name, params (string Name, byte[] Contents)[] contents) =>
        (name, AsZip(contents));

    private static async Task<LocalOemDriverComparison.Report> Compare(
        byte[] zip, IReadOnlyList<DriverInfo>? devices = null,
        CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(zip, writable: false);
        return await LocalOemNestedArchiveComparison.CompareAsync(
            stream, devices ?? Devices, "x64", 10, 0, 26100,
            cancellationToken);
    }

    [Fact]
    public async Task OnlyNestedZipInfRecoversRealNvidiaAndKillerPnP()
    {
        var package = AsZip(
            InnerZip("private/order-8898-nvidia.zip",
                Inf("order/RTX.inf", NvidiaInf)),
            InnerZip("private/order-8898-killer.zip",
                Inf("order/Killer.inf", KillerInf)));

        var report = await Compare(package);
        Assert.Equal(2, report.InfExamined);
        Assert.Equal(2, report.TotalMatchedDevices);
        Assert.Equal(2, report.Findings.Count);
        Assert.Equal(2, report.PotentialNewerDevices);
        Assert.Equal([0, 1], report.MatchedDeviceOrdinals);
        Assert.Contains(report.Findings, f =>
            f.DeviceName == "Microsoft Basic Display Adapter" &&
            f.HasExactHardwareId &&
            f.IsPotentialNewer);
        Assert.Contains(report.Findings, f =>
            f.DeviceName == "Killer Wi-Fi" &&
            f.HasExactHardwareId &&
            f.IsPotentialNewer);
        Assert.All(report.Findings, row =>
            Assert.StartsWith("Archivio ", row.InfName));
        Assert.Contains(report.Notes, note =>
            note.Contains("2 archivi ZIP/CAB interni"));
        Assert.DoesNotContain("8898", report.Summary);
        Assert.DoesNotContain("8898", string.Join(" ", report.Notes));
        Assert.DoesNotContain("PRIVATE_INSTANCE", report.Summary);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
    }

    [Fact]
    public async Task RepeatedGpuInOuterAndInnerUsesBestVersionAndNoDoubleCount()
    {
        var package = AsZip(
            Inf("private/gpu-older.inf", NvidiaInf),
            InnerZip("private/GPU-newer.zip",
                Inf("private/RTX-newer.inf", NvidiaNewerInf)));
        var report = await Compare(package);

        var winner = Assert.Single(report.Findings);
        Assert.Equal("33.0.0.1", winner.PackageVersion);
        Assert.Equal(1, report.TotalMatchedDevices);
        Assert.Single(report.MatchedDeviceOrdinals!);
        Assert.Equal(1, report.PotentialNewerDevices);
        Assert.True(report.AdditionalMatchesOmitted >= 1);
        Assert.DoesNotContain("gpu-older", report.Summary);
        Assert.DoesNotContain("RTX-newer", winner.InfName);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public async Task NestedRealtekZipIsRecognizedAlongsideOuterNvidia()
    {
        var package = AsZip(
            Inf("base/gpu.inf", NvidiaInf),
            InnerZip("audio/realtek.zip",
                Inf("media/realtek.inf", RealtekInf)));
        var report = await Compare(package);
        Assert.Equal(2, report.TotalMatchedDevices);
        Assert.Contains(report.Findings, row =>
            row.DeviceName == "High Definition Audio Device" &&
            row.IsPotentialNewer);
        Assert.Contains(report.Findings, row =>
            row.DeviceName == "Microsoft Basic Display Adapter" &&
            row.IsPotentialNewer);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public async Task MoreThanEightNestedArchivesRejectsEntirePackage()
    {
        var nested = Enumerable.Range(0, 9)
            .Select(i => InnerZip($"subfolder/{i:D2}.zip",
                Inf("nv.inf", NvidiaInf)))
            .ToArray();
        var package = AsZip(nested);
        await Assert.ThrowsAsync<InvalidDataException>(() => Compare(package));
    }

    [Fact]
    public async Task CorruptInnerZipRejectsBatchInsteadOfPretendingEmpty()
    {
        var corrupt = new byte[64];
        corrupt[0] = (byte)'P';
        corrupt[1] = (byte)'K';
        corrupt[2] = 3;
        corrupt[3] = 4;
        var package = AsZip(
            Inf("nv.inf", NvidiaInf),
            ("private/broken.zip", corrupt));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Compare(package));
    }

    [Fact]
    public async Task UnsafeZipPathInInnerFailsClosed()
    {
        var package = AsZip(
            InnerZip("drivers/inner.zip",
                Inf("../escape.inf", NvidiaInf)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Compare(package));
    }

    [Fact]
    public async Task SecondLevelZipRecoversNvidiaKillerAndRealtekWithoutDoubleCount()
    {
        var package = AsZip(
            Inf("gpu-baseline.inf", NvidiaInf),
            InnerZip("GPU/level-one.zip",
                InnerZip("GPU/level-two.zip",
                    Inf("private-rtx.inf", NvidiaNewerInf),
                    Inf("killer.inf", KillerInf))),
            InnerZip("Audio/realtek.zip",
                Inf("realtek.inf", RealtekInf)));
        var report = await Compare(package);

        Assert.Equal(4, report.InfExamined);
        Assert.Equal(3, report.TotalMatchedDevices);
        Assert.Equal([0, 1, 2], report.MatchedDeviceOrdinals);
        Assert.Equal(3, report.PotentialNewerDevices);
        Assert.Equal("33.0.0.1", Assert.Single(report.Findings,
            f => f.DeviceName == "Microsoft Basic Display Adapter").PackageVersion);
        Assert.Contains(report.Findings, f => f.DeviceName == "Killer Wi-Fi");
        Assert.Contains(report.Findings, f =>
            f.DeviceName == "High Definition Audio Device");
        Assert.True(report.AdditionalMatchesOmitted >= 1);
        Assert.Contains(report.Notes,
            note => note.Contains("3 archivi ZIP/CAB interni") &&
                note.Contains("due livelli"));
        Assert.False(report.HasUninspectedNestedArchives);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain("private-rtx", report.Summary);
        Assert.DoesNotContain("private-rtx",
            string.Join(" ", report.Findings.Select(f => f.InfName)));
    }

    [Fact]
    public async Task ThirdLevelZipRemainsExplicitlyPartialWhileSecondLevelInfIsRead()
    {
        var package = AsZip(
            InnerZip("depth-1.zip",
                InnerZip("depth-2.zip",
                    Inf("killer.inf", KillerInf),
                    InnerZip("depth-3.zip", Inf("nvidia.inf", NvidiaInf)))));
        var report = await Compare(package);

        var match = Assert.Single(report.Findings);
        Assert.Equal("Killer Wi-Fi", match.DeviceName);
        Assert.Equal(1, report.TotalMatchedDevices);
        Assert.Equal(1, report.InfExamined);
        Assert.True(report.HasUninspectedNestedArchives);
        Assert.Contains(report.Notes,
            note => note.Contains("oltre il secondo livello NON analizzati"));
        Assert.DoesNotContain(report.Findings,
            finding => finding.DeviceName == "Microsoft Basic Display Adapter");
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public async Task TotalNestedArchiveBudgetIsSharedAcrossBranches()
    {
        var package = AsZip(
            InnerZip("00-parent.zip",
                InnerZip("a.zip", Inf("nv.inf", NvidiaInf)),
                InnerZip("b.zip", Inf("nv.inf", NvidiaInf)),
                InnerZip("c.zip", Inf("nv.inf", NvidiaInf)),
                InnerZip("d.zip", Inf("nv.inf", NvidiaInf))),
            InnerZip("01.zip", Inf("nv.inf", NvidiaInf)),
            InnerZip("02.zip", Inf("nv.inf", NvidiaInf)),
            InnerZip("03.zip", Inf("nv.inf", NvidiaInf)),
            InnerZip("04.zip", Inf("nv.inf", NvidiaInf)));

        // 5 level-one children + 4 level-two = 9 > global budget 8.
        await Assert.ThrowsAsync<InvalidDataException>(() => Compare(package));
    }

    [Fact]
    public async Task CorruptSecondLevelZipFailsWholePackageClosed()
    {
        var corrupt = new byte[64];
        corrupt[0] = (byte)'P';
        corrupt[1] = (byte)'K';
        corrupt[2] = 3;
        corrupt[3] = 4;
        var package = AsZip(
            Inf("valid.inf", NvidiaInf),
            InnerZip("mid.zip", ("broken.zip", corrupt)));

        await Assert.ThrowsAsync<InvalidDataException>(() => Compare(package));
    }

    [Fact]
    public async Task TraversalInsideSecondLevelZipFailsWholePackageClosed()
    {
        var package = AsZip(
            InnerZip("mid.zip",
                InnerZip("leaf.zip", Inf("../escape.inf", KillerInf))));

        await Assert.ThrowsAsync<InvalidDataException>(() => Compare(package));
    }

    [Fact]
    public async Task ExistingDirectZipWithoutChildRemainsUnchanged()
    {
        var zip = AsZip(Inf("gpu.inf", NvidiaInf));
        var baseline = new MemoryStream(zip, writable: false);
        using (baseline)
        {
            var oldReport = LocalOemDriverComparison.Compare(
                baseline, Devices, "x64", 10, 0, 26100);
            var newReport = await Compare(zip);
            Assert.Equal(oldReport.Summary, newReport.Summary);
            Assert.Equal(oldReport.Findings, newReport.Findings);
            Assert.Equal(oldReport.MatchedDeviceOrdinals,
                newReport.MatchedDeviceOrdinals);
            Assert.Equal(oldReport.Notes, newReport.Notes);
        }
    }

    [Fact]
    public async Task AlreadyCancelledRequestDoesNotOpenNestedArchive()
    {
        var package = AsZip(InnerZip("nv.zip", Inf("nv.inf", NvidiaInf)));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Compare(package, cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task NestedMsiAndExeCannotBeUsedAsZipOrDriverEvidence()
    {
        var innerExe = new byte[256];
        innerExe[0] = (byte)'M';
        innerExe[1] = (byte)'Z';
        var package = AsZip(
            ("private/vendor-setup.exe", innerExe),
            ("private/firmware.msi", new byte[32]));
        var report = await Compare(package);
        Assert.Empty(report.Findings);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
    }

    [Fact]
    public async Task NestedCabMadeWithWindowsMakecabCorrelatesKillerWithoutExecution()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Directory.CreateTempSubdirectory(
            "OmegaDrive-NestedCabTest-").FullName;
        try
        {
            var infPath = Path.Combine(root, "killer.inf");
            var cabPath = Path.Combine(root, "driver.cab");
            await File.WriteAllTextAsync(infPath, KillerInf,
                new UTF8Encoding(false));
            var exe = Path.Combine(Environment.SystemDirectory, "makecab.exe");
            Assert.True(File.Exists(exe));
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root
            };
            start.ArgumentList.Add(infPath);
            start.ArgumentList.Add(cabPath);
            using (var process = Process.Start(start))
            {
                Assert.NotNull(process);
                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(45));
                await process.WaitForExitAsync(timeout.Token);
                Assert.Equal(0, process.ExitCode);
            }

            var cab = await File.ReadAllBytesAsync(cabPath);
            var package = AsZip(
                ("vendor/driver/Network.cab", cab),
                InnerZip("GPU/Graphics.zip", Inf("nv.inf", NvidiaInf)));
            var report = await Compare(package);
            Assert.Equal(2, report.TotalMatchedDevices);
            Assert.Equal(2, report.InfExamined);
            Assert.Equal(2, report.PotentialNewerDevices);
            Assert.Contains(report.Findings,
                f => f.DeviceName == "Killer Wi-Fi");
            Assert.Contains(report.Findings,
                f => f.DeviceName == "Microsoft Basic Display Adapter");
            Assert.False(report.IsInstallable);
            Assert.False(report.IsVerifiedCompatible);
            Assert.Contains(report.Notes,
                note => note.Contains("2 archivi ZIP/CAB"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NestedZipWithinPeZipOverlayIsReadWithoutRunningExe()
    {
        var outer = AsZip(
            InnerZip("GPU/driver.zip", Inf("private-nvidia.inf", NvidiaInf)));
        var header = new byte[0x400];
        header[0] = (byte)'M';
        header[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x3c), 0x80);
        header[0x80] = (byte)'P';
        header[0x81] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x84), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x86), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x94), 0xF0);
        var section = 0x80 + 24 + 0xF0;
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(section + 16), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(section + 20), 0x200);

        using var executable = new MemoryStream();
        executable.Write(header);
        executable.Write(outer);
        executable.Position = 0;
        var report = await LocalOemPeZipComparison.CompareAsync(
            executable, Devices, "x64", 10, 0, 26100);
        var winner = Assert.Single(report.Findings);
        Assert.Equal("32.0.15.7000", winner.PackageVersion);
        Assert.Equal(1, report.TotalMatchedDevices);
        Assert.StartsWith("EXE ZIP", winner.InfName);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.Contains(report.Notes, n => n.Contains("SENZA esecuzione"));
    }

    [Fact]
    public async Task BatchCombinesTwoParentsWithNestedNvidiaAndKillerWithoutDuplicates()
    {
        var first = await Compare(AsZip(
            InnerZip("a/gpu.zip", Inf("gpu.inf", NvidiaInf))));
        var second = await Compare(AsZip(
            InnerZip("b/gpu.zip", Inf("new-gpu.inf", NvidiaNewerInf)),
            InnerZip("b/network.zip", Inf("killer.inf", KillerInf))));
        var batch = LocalOemArchiveBatchSummary.Combine([first, second]);

        Assert.Equal(2, batch.DistinctMatchedDevices);
        Assert.Equal(3, batch.TotalAssociations);
        Assert.Equal(1, batch.RepeatedAssociations);
        Assert.Equal(2, batch.Findings.Count);
        Assert.Contains(batch.Findings, f =>
            f.DeviceName == "Microsoft Basic Display Adapter" &&
            f.PackageVersion == "33.0.0.1");
        Assert.Contains(batch.Findings, f => f.DeviceName == "Killer Wi-Fi");
        Assert.False(batch.IsInstallable);
        Assert.False(batch.IsVerifiedCompatible);
    }

    [Fact]
    public async Task ValidCabWithNoInfHasKnownEmptyMatchedSetInNestedCombine()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var root = Directory.CreateTempSubdirectory(
            "OmegaDrive-NestedNoInfTest-").FullName;
        try
        {
            var input = Path.Combine(root, "notes.txt");
            var cabPath = Path.Combine(root, "notes.cab");
            await File.WriteAllTextAsync(input, "not an INF");
            var start = new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "makecab.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root
            };
            start.ArgumentList.Add(input);
            start.ArgumentList.Add(cabPath);
            using (var proc = Process.Start(start))
            {
                Assert.NotNull(proc);
                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(45));
                await proc.WaitForExitAsync(timeout.Token);
                Assert.Equal(0, proc.ExitCode);
            }

            var package = AsZip(
                ("notes.cab", await File.ReadAllBytesAsync(cabPath)),
                InnerZip("gpu.zip", Inf("nv.inf", NvidiaInf)));
            var report = await Compare(package);
            Assert.Equal(1, report.TotalMatchedDevices);
            Assert.Single(report.Findings);
            Assert.Equal([0], report.MatchedDeviceOrdinals);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
