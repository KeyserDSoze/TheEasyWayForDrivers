using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// End-to-end CAB test with Windows built-in makecab.exe and expand.exe.
/// The only child processes are Windows OS tools; test INF is harmless text.
/// </summary>
public sealed class LocalOemCabComparisonTests
{
    private const string GpuId = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Inf = """
        [Version]
        Signature="$Windows NT$"
        Class=Display
        DriverVer=10/04/2026,32.0.15.7000
        [Manufacturer]
        Vendor=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        RTX4060=Install,PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA
        """;

    private static DriverInfo Device(string hardware = GpuId,
        string version = "31.0.15.5222") =>
        new("PRIVATE_DEVICE_INSTANCE_ID", "NVIDIA RTX 4060 Laptop GPU",
            "NVIDIA", "Display", "NVIDIA", version, null, "oem14.inf",
            true, true, 0, [hardware], []);

    private sealed class TemporaryCab : IDisposable
    {
        public string Folder { get; } =
            Directory.CreateTempSubdirectory("OmegaDrive-CabTest-").FullName;
        public string PathCab => Path.Combine(Folder, "package.cab");
        public void Dispose() => Directory.Delete(Folder, recursive: true);

        public Task CreateAsync(string fileName, string contents) =>
            CreateBytesAsync(fileName, new UTF8Encoding(false).GetBytes(contents));

        public async Task CreateBytesAsync(string fileName, byte[] contents)
        {
            var source = Path.Combine(Folder, fileName);
            await File.WriteAllBytesAsync(source, contents);
            var executable = Path.Combine(Environment.SystemDirectory, "makecab.exe");
            Assert.True(File.Exists(executable), "Windows makecab.exe is required for CAB tests.");

            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Folder
            };
            start.ArgumentList.Add(source);
            start.ArgumentList.Add(PathCab);

            using var process = Process.Start(start);
            Assert.NotNull(process);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.True(File.Exists(PathCab));
        }
    }

    /// <summary>
    /// Insert a standards-compliant opaque 20-byte CFHEADER reservation into
    /// a cabinet created by Windows makecab.exe. Offsets and cbCabinet are
    /// shifted without touching any CFDATA bytes or executing OEM software.
    /// Signed cabinets use this same CFHEADER reserve structure; these test
    /// bytes are NOT a signature or trusted OEM package.
    /// </summary>
    private static async Task AddOpaqueCabinetReservationAsync(string path)
    {
        var original = await File.ReadAllBytesAsync(path);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(
            original.AsSpan(30, 2)));
        const int additionalBytes = 24; // 4-byte reserve sizes + 20 opaque bytes
        var changed = new byte[original.Length + additionalBytes];
        original.AsSpan(0, 36).CopyTo(changed);
        original.AsSpan(36).CopyTo(changed.AsSpan(36 + additionalBytes));

        BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(8, 4),
            (uint)changed.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(16, 4),
            checked(BinaryPrimitives.ReadUInt32LittleEndian(
                original.AsSpan(16, 4)) + (uint)additionalBytes));
        BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(30, 2), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(36, 2), 20);
        changed[38] = 0; // cbCFFolder
        changed[39] = 0; // cbCFData

        var folderCount = BinaryPrimitives.ReadUInt16LittleEndian(
            original.AsSpan(26, 2));
        for (var i = 0; i < folderCount; i++)
        {
            var folderOffset = 36 + additionalBytes + i * 8;
            var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(
                changed.AsSpan(folderOffset, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(
                changed.AsSpan(folderOffset, 4),
                checked(dataOffset + (uint)additionalBytes));
        }

        await File.WriteAllBytesAsync(path, changed);
    }

    [Fact]
    public async Task WindowsExpandCanReadInfFromCabWithOptionalHeaderReserve()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var test = new TemporaryCab();
        await test.CreateAsync("display.inf", Inf);
        await AddOpaqueCabinetReservationAsync(test.PathCab);

        using (var source = File.OpenRead(test.PathCab))
        {
            var index = TheEasyWayForDrivers.Core.Security.OemCabIndex.Inspect(source);
            Assert.Equal("display.inf", Assert.Single(index.Entries).Path);
        }

        var report = await LocalOemCabComparison.CompareAsync(
            test.PathCab, [Device()], "x64", 10, 0, 26100);

        Assert.Equal(1, report.InfExamined);
        Assert.Equal(1, report.DevicesWithEvidence);
        Assert.Equal(1, report.PotentialNewerDevices);
        Assert.True(Assert.Single(report.Findings).HasExactHardwareId);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE_DEVICE_INSTANCE_ID", report.Summary);
    }

    [Fact]
    public async Task WindowsMakecabGeneratedCabCorrelatesGpuWithoutInstalling()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var test = new TemporaryCab();
        await test.CreateAsync("display.inf", Inf);

        var report = await LocalOemCabComparison.CompareAsync(
            test.PathCab, [Device()], "x64", 10, 0, 26100);
        Assert.Equal(1, report.InfExamined);
        Assert.Equal(1, report.DevicesExamined);
        var row = Assert.Single(report.Findings);
        Assert.True(row.HasExactHardwareId);
        Assert.True(row.IsPotentialNewer);
        Assert.Equal("32.0.15.7000", row.PackageVersion);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE_DEVICE_INSTANCE_ID", report.Summary);
        Assert.Contains("expand.exe", string.Join(" ", report.Notes));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealWindowsCabSupportsUnicodeBeAndAnsiInf(bool unicodeBe)
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var test = new TemporaryCab();
        byte[] raw;
        if (unicodeBe)
        {
            var encoding = new UnicodeEncoding(true, true, true);
            raw = [.. encoding.GetPreamble(), .. encoding.GetBytes(Inf)];
        }
        else
        {
            raw = Encoding.Latin1.GetBytes(
                Inf.Replace("Class=Display",
                    "Class=Display\n; commento con accento " + '\u00E9'));
        }

        await test.CreateBytesAsync("display.inf", raw);
        var report = await LocalOemCabComparison.CompareAsync(
            test.PathCab, [Device()], "x64", 10, 0, 26100);
        Assert.Equal(1, report.InfExamined);
        Assert.Equal(1, report.DevicesWithEvidence);
        Assert.Equal(1, report.PotentialNewerDevices);
        Assert.Equal(unicodeBe ? 0 : 1, report.LegacyAnsiCount);
        Assert.Equal(0, report.RejectedInfCount);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
    }

    [Fact]
    public async Task WindowsMakecabGeneratedCabWithUnrelatedInfReturnsZeroMatches()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var test = new TemporaryCab();
        await test.CreateAsync("display.inf", Inf);
        var report = await LocalOemCabComparison.CompareAsync(
            test.PathCab, [Device(@"PCI\VEN_8086&DEV_51F1")],
            "x64", 10, 0, 26100);
        Assert.Equal(1, report.InfExamined);
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.PotentialNewerDevices);
    }

    [Fact]
    public async Task NoInfInCabIsNotAnUpdateClaim()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var test = new TemporaryCab();
        await test.CreateAsync("notes.txt", "No driver data here.");
        var report = await LocalOemCabComparison.CompareAsync(
            test.PathCab, [Device()], "x64", 10, 0, 26100);
        Assert.Equal(0, report.InfExamined);
        Assert.Empty(report.Findings);
        Assert.Contains("nessun INF dichiarato", report.Notes[0]);
    }

    [Fact]
    public async Task ExeOrMsiAreNeverExecutedOrParsedAsCab()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var test = new TemporaryCab();
        var exe = Path.Combine(test.Folder, "vendor-setup.exe");
        await File.WriteAllTextAsync(exe, "DO NOT RUN");
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            LocalOemCabComparison.CompareAsync(
                exe, [Device()], "x64", 10, 0, 26100));
    }

    [Fact]
    public async Task MalformedCabIsRejectedBeforeLaunchingExpand()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var test = new TemporaryCab();
        await File.WriteAllBytesAsync(test.PathCab,
            [0x4d, 0x53, 0x43, 0x46, 0, 0, 0, 0]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            LocalOemCabComparison.CompareAsync(
                test.PathCab, [Device()], "x64", 10, 0, 26100));
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotExtractAnyCabFiles()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var test = new TemporaryCab();
        await test.CreateAsync("display.inf", Inf);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LocalOemCabComparison.CompareAsync(
                test.PathCab, [Device()], "x64", 10, 0, 26100,
                cancelled.Token));
    }
}
