using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #519: a ZIP file cannot also be an ancestor directory of any other
/// installer entry. Detect before touching staging, in both ZIP orders.
/// </summary>
public sealed class SetupPayloadAncestorCollisionTests
{
    private sealed class StageDirectory : IDisposable
    {
        public string FullName { get; } = Path.Combine(
            Path.GetTempPath(),
            "OmegaSetup-AncestorBoundary-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(FullName))
                Directory.Delete(FullName, recursive: true);
        }
    }

    private static MemoryStream Archive(
        params (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = zip.CreateEntry(path);
                if (path.EndsWith('/'))
                    continue;
                using var output = entry.Open();
                output.Write(Encoding.UTF8.GetBytes(content));
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static (string Path, string Content)[] WithExecutables(
        params (string Path, string Content)[] extra) =>
        [
            ("Service/App1.Service.exe", "SERVICE"),
            ("Desktop/App2.Desktop.exe", "DESKTOP"),
            .. extra
        ];

    [Theory]
    [InlineData("Service/runtime", "Service/runtime/child.dll")]
    [InlineData("Service/runtime", "Service/RUNTIME/child.dll")]
    [InlineData("Desktop/languages", "Desktop/LANGUAGES/it-IT.dll")]
    [InlineData("Service/vendor/driver", "Service/vendor/driver/x.dll")]
    [InlineData("Desktop/bundle", "Desktop/bundle/subdir/")]
    [InlineData("Service/runtime", "Service/runtime/subdir/")]
    public void FileUsedAsImplicitOrExplicitAncestorIsRefusedBeforeWrites(
        string fileName, string child)
    {
        foreach (var reversed in new[] { false, true })
        {
            var rows = reversed
                ? WithExecutables((child, "CHILD"), (fileName, "FILE"))
                : WithExecutables((fileName, "FILE"), (child, "CHILD"));
            using var zip = Archive(rows);
            using var staging = new StageDirectory();

            var error = Assert.Throws<InvalidDataException>(() =>
                SetupPayloadArchive.Extract(zip, staging.FullName));

            Assert.Contains("file/cartella", error.Message);
            Assert.DoesNotContain("runtime", error.Message);
            Assert.DoesNotContain("LANGUAGES", error.Message);
            Assert.False(Directory.Exists(staging.FullName));
        }
    }

    [Theory]
    [InlineData("Service/App1.Service.exe",
        "Service/App1.Service.exe/nested.dll")]
    [InlineData("Desktop/App2.Desktop.exe",
        "Desktop/App2.Desktop.exe/support.dll")]
    [InlineData("Service/App1.Service.exe",
        "Service/APP1.SERVICE.EXE/nested/")]
    public void MandatoryExecutableCannotAlsoBeAParentDirectory(
        string file, string child)
    {
        // The essential EXE exists, has bytes, and passes the existing
        // mandatory-file check. The last entry must still invalidate the
        // *entire* ZIP in preflight before the EXE can be staged.
        foreach (var reversed in new[] { false, true })
        {
            var rows = reversed
                ? new[] {
                    (child, "CHILD"),
                    ("Service/App1.Service.exe", "SERVICE"),
                    ("Desktop/App2.Desktop.exe", "DESKTOP")
                }
                : WithExecutables((child, "CHILD"));
            using var zip = Archive(rows);
            using var staging = new StageDirectory();
            var error = Assert.Throws<InvalidDataException>(() =>
                SetupPayloadArchive.Extract(zip, staging.FullName));
            Assert.Contains("file/cartella", error.Message);
            Assert.False(Directory.Exists(staging.FullName));
            Assert.DoesNotContain(file, error.Message);
        }
    }

    [Fact]
    public void LegitimateImplicitAndExplicitDirectoriesRemainExtractable()
    {
        using var zip = Archive(
            ("Service/", ""),
            ("Service/App1.Service.exe", "SERVICE"),
            ("Service/runtime/", ""),
            ("Service/runtime/hostfxr.dll", "HOST"),
            ("Service/runtime/native/win-x64.dll", "NATIVE"),
            ("Desktop/", ""),
            ("Desktop/App2.Desktop.exe", "DESKTOP"),
            ("Desktop/en-US/", ""),
            ("Desktop/en-US/resource.dll", "RESOURCE"));
        using var staging = new StageDirectory();

        SetupPayloadArchive.Extract(zip, staging.FullName);

        Assert.Equal("SERVICE", File.ReadAllText(Path.Combine(
            staging.FullName, "Service", "App1.Service.exe")));
        Assert.Equal("DESKTOP", File.ReadAllText(Path.Combine(
            staging.FullName, "Desktop", "App2.Desktop.exe")));
        Assert.Equal("NATIVE", File.ReadAllText(Path.Combine(
            staging.FullName, "Service", "runtime", "native",
            "win-x64.dll")));
        Assert.Equal("RESOURCE", File.ReadAllText(Path.Combine(
            staging.FullName, "Desktop", "en-US", "resource.dll")));
        Assert.Equal(5, Directory.GetFiles(staging.FullName, "*",
            SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void ExistingStagingLocationIsNotTouchedByInvalidZip()
    {
        using var zip = Archive(WithExecutables(
            ("Service/runtime", "FILE"),
            ("Service/runtime/library.dll", "COLLISION")));
        using var staging = new StageDirectory();
        Directory.CreateDirectory(staging.FullName);
        var sentinel = Path.Combine(staging.FullName, "sentinel.txt");
        File.WriteAllText(sentinel, "KEEP");

        Assert.Throws<InvalidDataException>(() =>
            SetupPayloadArchive.Extract(zip, staging.FullName));

        Assert.Equal("KEEP", File.ReadAllText(sentinel));
        Assert.Single(Directory.GetFiles(staging.FullName));
        Assert.False(Directory.Exists(Path.Combine(
            staging.FullName, "Service")));
    }
}
