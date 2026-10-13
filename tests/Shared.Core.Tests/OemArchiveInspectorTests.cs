using System.IO.Compression;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemArchiveInspectorTests
{
    private static MemoryStream BuildZip(params string[] names)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in names)
            {
                var entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("test");
            }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void ListsInfCatSysButNeverMarksInstallable()
    {
        using var zip = BuildZip("drivers/main.inf", "drivers/main.cat", "drivers/main.sys", "README.txt");
        var report = OemArchiveInspector.Inspect(zip);
        Assert.Equal(OemArchiveInspector.Format.Zip, report.ArchiveFormat);
        Assert.Equal(4, report.Entries.Count);
        Assert.Contains(report.Entries, e => e.Kind == OemArchiveInspector.FileKind.Inf);
        Assert.Contains(report.Entries, e => e.Kind == OemArchiveInspector.FileKind.Cat);
        Assert.Contains(report.Entries, e => e.Kind == OemArchiveInspector.FileKind.Sys);
        Assert.False(report.IsInstallable);
        Assert.False(report.ContentsAuthenticated);
        Assert.True(report.RequiresManualReview);
    }

    [Theory]
    [InlineData("../outside.sys")]
    [InlineData("/absolute.sys")]
    [InlineData("C:/windows/file.sys")]
    [InlineData("a/./same.inf")]
    [InlineData("a//double.cat")]
    public void RejectsUnsafeZipMemberName(string name)
    {
        using var zip = BuildZip(name);
        Assert.Throws<InvalidDataException>(() => OemArchiveInspector.Inspect(zip));
    }

    [Fact]
    public void RejectsCaseInsensitiveDuplicateNames()
    {
        using var zip = BuildZip("drivers/X.inf", "drivers/x.inf");
        Assert.Throws<InvalidDataException>(() => OemArchiveInspector.Inspect(zip));
    }

    [Fact]
    public void CabIsIdentifiedButNotEnumerated()
    {
        using var stream = new MemoryStream(new byte[] { 0x4d, 0x53, 0x43, 0x46, 0, 0, 0, 0 });
        var report = OemArchiveInspector.Inspect(stream);
        Assert.Equal(OemArchiveInspector.Format.Cab, report.ArchiveFormat);
        Assert.Empty(report.Entries);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void NeverExtractsArchiveContents()
    {
        using var zip = BuildZip("sample.inf");
        var before = zip.Length;
        var report = OemArchiveInspector.Inspect(zip);
        Assert.Equal(before, zip.Length);
        Assert.Single(report.Entries);
    }
}
