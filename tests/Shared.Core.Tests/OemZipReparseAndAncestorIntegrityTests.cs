using System.IO.Compression;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #520: every OEM ZIP index, including PE-ZIP and nested archive probes,
/// must reject Windows DOS reparse entries and file-as-directory aliases.
/// No metadata-only match can authorize staging or installing drivers.
/// </summary>
public sealed class OemZipReparseAndAncestorIntegrityTests
{
    private static MemoryStream Zip(
        params (string Name, int ExternalAttributes)[] entries)
    {
        var bytes = new MemoryStream();
        using (var writer = new ZipArchive(
            bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, attributes) in entries)
            {
                var entry = writer.CreateEntry(name);
                if (attributes != 0)
                    entry.ExternalAttributes = attributes;
                if (name.EndsWith('/'))
                    continue;
                using var stream = entry.Open();
                stream.WriteByte(0x41);
            }
        }
        bytes.Position = 0;
        return bytes;
    }

    [Theory]
    [InlineData("drivers/graphics.inf")]
    [InlineData("drivers/graphics.cat")]
    [InlineData("drivers/graphics.sys")]
    [InlineData("drivers/notes.txt")]
    [InlineData("drivers/platform/")]
    public void WindowsReparsePointEntriesNeverProvideTrustedOemZipMetadata(
        string suspect)
    {
        using var data = Zip(
            ("drivers/main.inf", 0),
            (suspect, (int)FileAttributes.ReparsePoint),
            ("drivers/platform.sys", 0));

        var error = Assert.Throws<InvalidDataException>(() =>
            OemArchiveInspector.Inspect(data));
        Assert.Contains("reparse point", error.Message);
        Assert.DoesNotContain("graphics", error.Message);
        Assert.DoesNotContain("drivers", error.Message);

        data.Position = 0;
        using var zip = new ZipArchive(data, ZipArchiveMode.Read,
            leaveOpen: true);
        Assert.Throws<InvalidDataException>(() =>
            OemArchiveInspector.CreateValidatedZipEntryIndex(zip));
    }

    [Fact]
    public void UnixSymlinksRemainRejectedAlongsideWindowsReparsePoints()
    {
        using var data = Zip(
            ("drivers/main.inf", 0),
            ("drivers/alias.cat", unchecked((int)0xA0000000u)));
        var error = Assert.Throws<InvalidDataException>(() =>
            OemArchiveInspector.Inspect(data));
        Assert.Contains("Collegamenti", error.Message);
        Assert.DoesNotContain("alias.cat", error.Message);
    }

    [Theory]
    [InlineData("drivers/chipset",
        "drivers/chipset/nvidia.inf")]
    [InlineData("drivers/chipset",
        "drivers/CHIPSET/nvidia.inf")]
    [InlineData("drivers/chipset",
        @"drivers\chipset\killer.inf")]
    [InlineData("drivers/nested/platform",
        "drivers/nested/platform/realtek.inf")]
    [InlineData("drivers/chipset",
        "drivers/chipset/")]
    [InlineData("drivers/chipset",
        @"drivers\CHIPSET\")]
    public void FileAsImplicitOrExplicitDirectoryFailsInBothEntryOrders(
        string file, string child)
    {
        foreach (var reverse in new[] { false, true })
        {
            var inputs = reverse
                ? new[] { (child, 0), (file, 0) }
                : new[] { (file, 0), (child, 0) };
            using var data = Zip(inputs);
            var error = Assert.Throws<InvalidDataException>(() =>
                OemArchiveInspector.Inspect(data));
            Assert.Contains("file/cartella", error.Message);
            Assert.DoesNotContain("chipset", error.Message);

            data.Position = 0;
            using var archive = new ZipArchive(
                data, ZipArchiveMode.Read, leaveOpen: true);
            Assert.Throws<InvalidDataException>(() =>
                OemArchiveInspector.CreateValidatedZipEntryIndex(archive));
        }
    }

    [Fact]
    public void ValidDirectoryEntriesAndWindowsBackslashesRemainSupported()
    {
        using var data = Zip(
            ("drivers/", 0),
            ("drivers/chipset/", 0),
            ("drivers/chipset/main.inf", 0),
            (@"drivers\network\killer.INF", 0),
            ("drivers/chipset/main.cat", (int)FileAttributes.Archive),
            ("drivers/chipset/main.sys", 0));
        var report = OemArchiveInspector.Inspect(data);

        Assert.Equal(OemArchiveInspector.Format.Zip, report.ArchiveFormat);
        Assert.Equal(4, report.Entries.Count);
        Assert.Contains(report.Entries, item =>
            item.Path == "drivers/chipset/main.inf" &&
            item.Kind == OemArchiveInspector.FileKind.Inf);
        Assert.Contains(report.Entries, item =>
            item.Path == "drivers/network/killer.INF" &&
            item.Kind == OemArchiveInspector.FileKind.Inf);
        Assert.False(report.IsInstallable);
        Assert.False(report.ContentsAuthenticated);

        data.Position = 0;
        using var archive = new ZipArchive(
            data, ZipArchiveMode.Read, leaveOpen: true);
        var index = OemArchiveInspector.CreateValidatedZipEntryIndex(archive);
        Assert.Equal(6, index.Count);
        Assert.True(index.ContainsKey("drivers/network/killer.INF"));
    }

    [Fact]
    public void MixedSlashCaseCollisionDoesNotExposeInfAsAValidMatch()
    {
        using var data = Zip(
            ("drivers/", 0),
            ("drivers/Platform", 0),
            (@"DRIVERS\platform\nvidia.inf", 0),
            (@"drivers\platform\killer.inf", 0));
        var error = Assert.Throws<InvalidDataException>(() =>
            OemArchiveInspector.Inspect(data));
        Assert.Contains("file/cartella", error.Message);
        Assert.DoesNotContain("nvidia", error.Message);
        Assert.DoesNotContain("killer", error.Message);
    }
}
