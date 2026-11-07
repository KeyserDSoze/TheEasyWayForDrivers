using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemPeZipOverlayTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string GpuInf = """
        [Version]
        Signature="$Windows NT$"
        Class=Display
        DriverVer=10/04/2026,32.0.15.7000
        [Manufacturer]
        Vendor=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        RTX4060=Install,PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA
        """;
    private const string KillerInf = """
        [Version]
        Signature="$Windows NT$"
        Class=Net
        DriverVer=10/04/2026,24.0.11.2120
        [Manufacturer]
        Vendor=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        KillerWifi=Install,PCI\VEN_8086&DEV_51F1
        """;

    private static DriverInfo Device(string name, string id, string version) =>
        new("PRIVATE_DEVICE_INSTANCE", name, "Microsoft", "Display",
            "Microsoft", version, null, "oem10.inf", true, true, 0,
            [id], []);

    private static MemoryStream Zip(params (string Name, string Text)[] entries)
    {
        var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var (name, contents) in entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(contents);
            }
        }
        output.Position = 0;
        return output;
    }

    /// <summary>
    /// Synthetic PE32+ stub with a section occupying [0x200, 0x400).
    /// An actual standard ZIP follows at 0x400, never executed.
    /// </summary>
    private static MemoryStream PeWithOverlay(
        Stream? overlay, int prefixBytes = 0,
        bool falseZipInsidePeSection = false)
    {
        var header = new byte[0x400];
        header[0] = (byte)'M';
        header[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x3c), 0x80);
        header[0x80] = (byte)'P';
        header[0x81] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x84), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x86), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x94), 0xF0);
        var table = 0x80 + 24 + 0xF0;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(table + 16), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(table + 20), 0x200);

        if (falseZipInsidePeSection)
        {
            header[0x250] = (byte)'P';
            header[0x251] = (byte)'K';
            header[0x252] = 0x03;
            header[0x253] = 0x04;
        }

        var result = new MemoryStream();
        result.Write(header);
        if (prefixBytes > 0)
            result.Write(new byte[prefixBytes]);
        if (overlay is not null)
        {
            overlay.Position = 0;
            overlay.CopyTo(result);
        }
        result.Position = 0;
        return result;
    }

    private static LocalOemDriverComparison.Report Compare(Stream exe,
        IReadOnlyList<DriverInfo>? devices = null) =>
        LocalOemPeZipComparison.Compare(exe,
            devices ?? [Device("Microsoft Basic Display Adapter", Gpu,
                "31.0.15.5222")], "x64", 10, 0, 26100);

    [Fact]
    public void PeExeWithRealZipOverlayCorrelatesNvidiaWithoutExecutingIt()
    {
        using var zip = Zip(("PRIVATE-ORDER-12345/RTX.inf", GpuInf));
        using var exe = PeWithOverlay(zip, falseZipInsidePeSection: true);

        var report = Compare(exe);
        var finding = Assert.Single(report.Findings);
        Assert.Equal(1, report.InfExamined);
        Assert.True(finding.HasExactHardwareId);
        Assert.True(finding.IsPotentialNewer);
        Assert.Equal("32.0.15.7000", finding.PackageVersion);
        Assert.Equal("EXE ZIP · INF 001", finding.InfName);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.Contains("SENZA esecuzione", string.Join(" ", report.Notes));
        Assert.DoesNotContain("PRIVATE-ORDER", report.Summary);
        Assert.DoesNotContain("12345", report.Summary);
        Assert.DoesNotContain("PRIVATE_DEVICE_INSTANCE", report.Summary);
    }

    [Fact]
    public void MultipleInfFilesFromSignedPeStubKeepKillerAndNvidiaSeparated()
    {
        using var zip = Zip(("GPU/rtx.inf", GpuInf),
            ("NETWORK/killer.inf", KillerInf));
        using var exe = PeWithOverlay(zip, prefixBytes: 512);
        var report = Compare(exe,
        [
            Device("NVIDIA RTX 4060", Gpu, "31.0.0.0"),
            Device("Killer WiFi", Killer, "23.0.0.0")
        ]);

        Assert.Equal(2, report.InfExamined);
        Assert.Equal(2, report.DevicesWithEvidence);
        Assert.Equal(2, report.PotentialNewerDevices);
        Assert.Contains(report.Findings, row => row.DeviceName == "NVIDIA RTX 4060");
        Assert.Contains(report.Findings, row => row.DeviceName == "Killer WiFi");
        Assert.All(report.Findings, row =>
        {
            Assert.StartsWith("EXE ZIP · INF", row.InfName);
            Assert.True(row.IsPotentialNewer);
        });
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void ProperZipOverlayCanBeStreamedWithoutCopyingExeIntoMemory()
    {
        using var zip = Zip(("rtx.inf", GpuInf));
        using var exe = PeWithOverlay(zip);
        using var sliced = OemPeZipOverlay.Open(exe);
        Assert.True(sliced.CanRead);
        Assert.True(sliced.CanSeek);
        Assert.False(sliced.CanWrite);
        Assert.True(sliced.Length < exe.Length);
        var prefix = new byte[4];
        Assert.Equal(4, sliced.Read(prefix, 0, 4));
        Assert.Equal([0x50, 0x4b, 0x03, 0x04], prefix);
        Assert.Throws<NotSupportedException>(() =>
            sliced.Write(prefix, 0, prefix.Length));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            sliced.Position = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            sliced.Position = sliced.Length + 1);
        sliced.Position = 0;
        Assert.Equal(OemArchiveInspector.Format.Zip,
            OemArchiveInspector.Inspect(sliced).ArchiveFormat);
    }

    [Fact]
    public void FakeZipInPeRsrcSectionNeverPassesAsAppendedArchive()
    {
        using var exe = PeWithOverlay(null,
            falseZipInsidePeSection: true);
        Assert.Throws<NotSupportedException>(() =>
            OemPeZipOverlay.Open(exe));
    }

    [Fact]
    public void NonPeOrInvalidPeHeaderMustFailClosed()
    {
        using var zip = Zip(("gpu.inf", GpuInf));
        using var exe = PeWithOverlay(zip);
        var copy = exe.ToArray();
        copy[0] = (byte)'Q';
        using var notExe = new MemoryStream(copy);
        Assert.Throws<NotSupportedException>(() =>
            OemPeZipOverlay.Open(notExe));

        var corrupted = exe.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            corrupted.AsSpan(0x3c), int.MaxValue);
        using var badPointer = new MemoryStream(corrupted);
        Assert.Throws<InvalidDataException>(() =>
            OemPeZipOverlay.Open(badPointer));

        corrupted = exe.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            corrupted.AsSpan(0x80 + 24 + 0xF0 + 20), 0xFFFF0000);
        using var badSection = new MemoryStream(corrupted);
        Assert.Throws<InvalidDataException>(() =>
            OemPeZipOverlay.Open(badSection));
    }

    [Fact]
    public void SevenZipOrProprietaryExePayloadsAreExplicitlyUnsupported()
    {
        using var sevenZ = new MemoryStream(new byte[]
        {
            0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x00
        });
        using var exe = PeWithOverlay(sevenZ);
        var error = Assert.Throws<NotSupportedException>(() =>
            OemPeZipOverlay.Open(exe));
        Assert.Contains("7z/NSIS/Inno", error.Message);
        Assert.Throws<NotSupportedException>(() => Compare(exe));
    }

    [Fact]
    public void MalformedZipWithPkOverlayCannotPretendNoDriversExist()
    {
        using var bogusZip = new MemoryStream(new byte[]
        {
            0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00
        });
        using var exe = PeWithOverlay(bogusZip);
        Assert.Throws<InvalidDataException>(() => Compare(exe));
    }

    [Fact]
    public void ArchiveTraversalInsideExeFailsExactlyLikeZipInspection()
    {
        using var malicious = Zip(("../secrets.inf", GpuInf));
        using var exe = PeWithOverlay(malicious);
        Assert.Throws<InvalidDataException>(() => Compare(exe));
    }

    [Fact]
    public void HighCompressionRatioInsideExeIsBlockedByExistingZipLimits()
    {
        using var archive = Zip(("huge.txt", new string('X', 6 * 1024 * 1024)));
        using var exe = PeWithOverlay(archive);
        Assert.Throws<InvalidDataException>(() => Compare(exe));
    }

    [Fact]
    public void OverlongPrefixCannotBeMistakenForSupportedPeZipSfx()
    {
        using var zip = Zip(("gpu.inf", GpuInf));
        using var exe = PeWithOverlay(zip,
            prefixBytes: checked((int)OemPeZipOverlay.MaximumOverlayPrefixBytes + 16));
        Assert.Throws<NotSupportedException>(() => OemPeZipOverlay.Open(exe));
    }

    [Fact]
    public void OriginalExeStreamRemainsOwnedByCaller()
    {
        using var zip = Zip(("gpu.inf", GpuInf));
        using var exe = PeWithOverlay(zip);
        using (var view = OemPeZipOverlay.Open(exe))
        {
            Assert.True(view.CanRead);
        }
        Assert.True(exe.CanRead);
        Assert.Equal(1, Compare(exe).DevicesWithEvidence);
    }
}
