using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class LargeOemInfFourMegabyteTests
{
    private const string NvidiaGpu =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_12345678";
    private const string KillerWifi =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_56781234";

    private static DriverInfo Device(string id, string name = "Generic Microsoft Device") =>
        new("LOCAL_PRIVATE_DEVICE_INSTANCE", name, "Microsoft", "Display",
            "Microsoft", "31.0.15.6000", null, "oem23.inf",
            true, true, 0, [id], []);

    private static string LargeInf(string target, int padLines = 1400)
    {
        var data = new StringBuilder(2 * 1024 * 1024);
        data.AppendLine("[Version]");
        data.AppendLine("Signature=\"$Windows NT$\"");
        data.AppendLine("Class=Display");
        data.AppendLine("DriverVer=10/04/2026,32.0.16.2000");
        data.AppendLine("[Manufacturer]");
        data.AppendLine("%Vendor%=Models,NTamd64.10.0...22000");
        data.AppendLine("[Models.NTamd64.10.0...22000]");
        // Target ID deliberately comes after 600 model entries; the
        // generic 500-ID preview must not control the full device match.
        for (int i = 0; i < 620; i++)
            data.AppendLine($@"%Unrelated{i}%=Install,PCI\VEN_8086&DEV_{i:X4}&SUBSYS_00000000");
        // Large realistic text-like metadata/long comments must be bounded
        // but must not automatically exclude a valid PnP model.
        for (int i = 0; i < padLines; i++)
        {
            data.Append("; package metadata ").Append(i.ToString("D5")).Append(' ');
            data.Append('X', 950);
            data.AppendLine();
        }
        data.Append("%Target%=Install,").AppendLine(target);
        return data.ToString();
    }

    private static MemoryStream Zip(params (string Name, string Content)[] entries)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, contents) in entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(contents);
            }
        }
        buffer.Position = 0;
        return buffer;
    }

    [Fact]
    public void LargeInfGreaterThanOneMegabyteIsRecognizedAcrossDecoderAndTargetParser()
    {
        var inf = LargeInf(NvidiaGpu);
        Assert.True(Encoding.UTF8.GetByteCount(inf) > 1024 * 1024);
        Assert.True(Encoding.UTF8.GetByteCount(inf) < OemZipInfEvidence.MaxInfBytes);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(inf));
        var decoded = OemInfTextDecoder.ReadBounded(stream);
        Assert.Equal(OemInfTextDecoder.Kind.Utf8, decoded.Encoding);
        Assert.Equal(inf, decoded.Text);
        Assert.Equal(500, InfModelEvidence.ReadDeclaredIds(inf).Count);
        var exact = Assert.Single(InfModelEvidence.ReadMatchingIds(
            inf, [Device(NvidiaGpu)]));
        Assert.Equal(NvidiaGpu, exact.HardwareId);
        Assert.True(exact.IsPrimaryHardwareId);

        var platform = InfPlatformPreflight.Assess(inf, Device(NvidiaGpu),
            "x64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            platform.Result);
        Assert.Equal(2, platform.HardwareMatchStrength);
    }

    [Theory]
    [InlineData(NvidiaGpu)]
    [InlineData(KillerWifi)]
    public void OfficialStyleLargeInfZipIsComparedReadOnlyWithNoInstallOffer(string id)
    {
        using var zip = Zip(("driver/large.inf", LargeInf(id)));

        var report = LocalOemDriverComparison.Compare(
            zip, [Device(id)], "AMD64", 10, 0, 26100);

        Assert.Equal(1, report.InfExamined);
        Assert.Equal(1, report.InfWithDeclaredIds);
        var row = Assert.Single(report.Findings);
        Assert.Equal(2, row.HardwareMatchStrength);
        Assert.True(row.IsPotentialNewer);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain("LOCAL_PRIVATE_DEVICE_INSTANCE", report.Summary);
        Assert.DoesNotContain(id, report.Summary);
        Assert.Contains(report.Notes, n => n.Contains("500 ID"));
    }

    [Fact]
    public void ExtractedFolderLargeInfIsReadOnlyAndNotSkipped()
    {
        var folder = Directory.CreateTempSubdirectory("OmegaDrive-LargeOEM-");
        try
        {
            var path = Path.Combine(folder.FullName, "nvidia.inf");
            File.WriteAllText(path, LargeInf(NvidiaGpu), new UTF8Encoding(false));

            var report = LocalOemFolderComparison.Compare(
                folder.FullName, [Device(NvidiaGpu)], "x64", 10, 0, 26100);

            Assert.Equal(1, report.InfExamined);
            Assert.Single(report.Findings);
            Assert.False(report.IsInstallable);
            Assert.False(report.IsVerifiedCompatible);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void FourMegabyteCeilingStillRejectsOverLimitBytesOrText()
    {
        var oversize = new byte[OemZipInfEvidence.MaxInfBytes + 1];
        Assert.Throws<InvalidDataException>(() => OemInfTextDecoder.Decode(oversize));
        using var memory = new MemoryStream(oversize);
        Assert.Throws<InvalidDataException>(() => OemInfTextDecoder.ReadBounded(memory));
        Assert.Throws<InvalidDataException>(() =>
            InfModelEvidence.ReadDeclaredIds(new string('A', oversize.Length)));
        Assert.Throws<InvalidDataException>(() =>
            InfPlatformPreflight.Assess(new string('A', oversize.Length),
                Device(NvidiaGpu), "x64", 10, 0, 26100, false));
    }

    [Fact]
    public void LargeInfWithSimilarButDifferentHardwareIdDoesNotBecomeCandidate()
    {
        using var zip = Zip(("driver/large.inf", LargeInf(NvidiaGpu)));
        var report = LocalOemDriverComparison.Compare(zip,
            [Device(@"PCI\VEN_10DE&DEV_28A1&SUBSYS_12345678")],
            "AMD64", 10, 0, 26100);
        Assert.Empty(report.Findings);
        Assert.False(report.IsInstallable);
    }
}
