using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class SetupPayloadArchiveTests
{
    private static MemoryStream Zip(params (string Name, string Contents)[] files)
    {
        var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var (name, contents) in files)
            {
                var entry = zip.CreateEntry(name);
                if (name.EndsWith('/'))
                    continue;
                using var output = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(contents);
                output.Write(bytes);
            }
        }
        data.Position = 0;
        return data;
    }

    private static MemoryStream ValidZip() => Zip(
        ("Service/", ""),
        ("Service/App1.Service.exe", "service-bytes"),
        ("Service/runtime.dll", "a runtime assembly"),
        ("Desktop/", ""),
        ("Desktop/App2.Desktop.exe", "desktop-bytes"),
        ("Desktop/en-US/resource.dll", "translations"));

    private sealed class TemporaryDestination : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "OmegaSetupPayloadTests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    [Fact]
    public void ExtractsOnlyExpectedApplicationTreesAndPreservesBytes()
    {
        using var zip = ValidZip();
        using var temp = new TemporaryDestination();

        SetupPayloadArchive.Extract(zip, temp.Path);

        Assert.Equal("service-bytes", File.ReadAllText(
            System.IO.Path.Combine(temp.Path, "Service", "App1.Service.exe")));
        Assert.Equal("desktop-bytes", File.ReadAllText(
            System.IO.Path.Combine(temp.Path, "Desktop", "App2.Desktop.exe")));
        Assert.Equal("translations", File.ReadAllText(
            System.IO.Path.Combine(temp.Path, "Desktop", "en-US", "resource.dll")));
        Assert.Equal(4, Directory.GetFiles(temp.Path, "*",
            SearchOption.AllDirectories).Length);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("Service/../outside.txt")]
    [InlineData("Service/sub/../../outside.txt")]
    [InlineData("Evil/rogue.exe")]
    [InlineData("Service/notes.txt:ads")]
    [InlineData("Service\\rogue.dll")]
    [InlineData("/Service/rogue.dll")]
    [InlineData("Service/CON.txt")]
    [InlineData("Desktop/NUL")]
    [InlineData("Service/COM1.dll")]
    [InlineData("Service/trailing./rogue.dll")]
    [InlineData("Desktop/trailing /rogue.dll")]
    [InlineData("Service/./file.dll")]
    [InlineData("Service/rogue\t.dll")]
    public void UnsafeEntriesAreRejectedBeforeAnyFileIsWritten(string name)
    {
        using var zip = Zip(
            ("Service/App1.Service.exe", "service"),
            ("Desktop/App2.Desktop.exe", "desktop"),
            (name, "unexpected"));
        using var temp = new TemporaryDestination();
        Assert.Throws<InvalidDataException>(() =>
            SetupPayloadArchive.Extract(zip, temp.Path));
        Assert.False(File.Exists(System.IO.Path.Combine(
            temp.Path, "Service", "App1.Service.exe")));
    }

    [Theory]
    [InlineData("Desktop/app2.desktop.EXE")]
    [InlineData("Service/app1.service.exe")]
    public void SameWindowsPathWithDifferentCaseCannotOverrideEntry(
        string duplicate)
    {
        using var zip = Zip(
            ("Service/App1.Service.exe", "service"),
            ("Desktop/App2.Desktop.exe", "desktop"),
            (duplicate, "overwritten"));
        using var temp = new TemporaryDestination();
        Assert.Throws<InvalidDataException>(() =>
            SetupPayloadArchive.Extract(zip, temp.Path));
        Assert.False(Directory.Exists(temp.Path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothEssentialExecutablesMustBePresent(bool includeService)
    {
        using var zip = includeService
            ? Zip(("Service/App1.Service.exe", "service"),
                ("Desktop/resource.dll", "resource"))
            : Zip(("Service/runtime.dll", "runtime"),
                ("Desktop/App2.Desktop.exe", "desktop"));
        using var temp = new TemporaryDestination();
        Assert.Throws<InvalidDataException>(() =>
            SetupPayloadArchive.Extract(zip, temp.Path));
        Assert.False(Directory.Exists(temp.Path));
    }

    [Fact]
    public void UnixSymlinkZipEntryIsRejected()
    {
        using var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var name in new[] {
                "Service/App1.Service.exe", "Desktop/App2.Desktop.exe" })
            {
                var e = zip.CreateEntry(name);
                using var output = e.Open();
                output.WriteByte(1);
            }
            var link = zip.CreateEntry("Service/symlink.dll");
            link.ExternalAttributes = unchecked((int)0xA0000000u);
            using var linkData = link.Open();
            linkData.WriteByte(1);
        }
        data.Position = 0;
        using var temp = new TemporaryDestination();
        Assert.Throws<InvalidDataException>(() =>
            SetupPayloadArchive.Extract(data, temp.Path));
        Assert.False(Directory.Exists(temp.Path));
    }

    [Fact]
    public void ExcessiveEntryCountIsRejectedBeforeWriting()
    {
        var entries = Enumerable.Range(0, SetupPayloadArchive.MaxEntries + 1)
            .Select(i => ($"Service/file{i:D5}.dll", "x")).ToArray();
        using var zip = Zip(entries);
        using var temp = new TemporaryDestination();
        Assert.Throws<InvalidDataException>(() =>
            SetupPayloadArchive.Extract(zip, temp.Path));
        Assert.False(Directory.Exists(temp.Path));
    }

    [Fact]
    public void AlreadyExistingFileIsNeverOverwritten()
    {
        using var zip = ValidZip();
        using var temp = new TemporaryDestination();
        var path = System.IO.Path.Combine(
            temp.Path, "Service", "App1.Service.exe");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "do not overwrite");

        Assert.Throws<IOException>(() =>
            SetupPayloadArchive.Extract(zip, temp.Path));
        Assert.Equal("do not overwrite", File.ReadAllText(path));
    }

    [Fact]
    public void CancellationStopsExtractionBeforeFilesystemModification()
    {
        using var zip = ValidZip();
        using var temp = new TemporaryDestination();
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            SetupPayloadArchive.Extract(zip, temp.Path, source.Token));
        Assert.False(Directory.Exists(temp.Path));
    }

    [Fact]
    public void EmptyMandatoryExecutableIsRejected()
    {
        using var zip = Zip(
            ("Service/App1.Service.exe", ""),
            ("Desktop/App2.Desktop.exe", "desktop"));
        using var temp = new TemporaryDestination();
        Assert.Throws<InvalidDataException>(() =>
            SetupPayloadArchive.Extract(zip, temp.Path));
        Assert.False(Directory.Exists(temp.Path));
    }
}
