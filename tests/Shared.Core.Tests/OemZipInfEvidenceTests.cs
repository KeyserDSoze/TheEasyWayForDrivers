using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemZipInfEvidenceTests
{
    private const string ValidInf = """
        [Manufacturer]
        Vendor=Models,NTamd64
        [Models.NTamd64]
        Sample=Install,PCI\VEN_8086&DEV_1234
        """;

    private static MemoryStream ZipWith(string name, byte[] bytes)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
            using var output = entry.Open();
            output.Write(bytes);
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void FindsDeclaredIdsWithoutClaimingCompatibility()
    {
        using var stream = ZipWith("drivers/example.inf", Encoding.UTF8.GetBytes(ValidInf));
        var result = OemZipInfEvidence.Inspect(stream);
        Assert.Equal(1, result.DeclaredIdCount);
        Assert.Equal("PCI\\VEN_8086&DEV_1234", Assert.Single(Assert.Single(result.InfFiles).DeclaredIds).HardwareId);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerified);
    }

    [Fact]
    public void Utf16LeBomIsAccepted()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(ValidInf)).ToArray();
        using var stream = ZipWith("example.inf", bytes);
        Assert.Equal(1, OemZipInfEvidence.Inspect(stream).DeclaredIdCount);
    }

    [Fact]
    public void InvalidUnicodeIsNotParsedAsEvidence()
    {
        using var stream = ZipWith("example.inf", [0xC3, 0x28]);
        var report = OemZipInfEvidence.Inspect(stream);
        Assert.Equal(0, report.DeclaredIdCount);
        Assert.NotEmpty(report.Warnings);
    }

    [Fact]
    public void NonZipIsNotEnumerated()
    {
        using var stream = new MemoryStream([0x4d, 0x53, 0x43, 0x46, 0, 0, 0, 0]);
        Assert.Empty(OemZipInfEvidence.Inspect(stream).InfFiles);
    }

    [Fact]
    public void RejectsTooManyInfFiles()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            for (var i = 0; i < OemZipInfEvidence.MaxInfFiles + 1; i++)
            {
                var entry = zip.CreateEntry("file" + i + ".inf");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("[Version]");
            }
        stream.Position = 0;
        using (stream)
            Assert.Throws<InvalidDataException>(() => OemZipInfEvidence.Inspect(stream));
    }
}
