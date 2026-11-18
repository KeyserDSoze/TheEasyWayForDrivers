using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #530: the setup's elevated scratch directory must not be created
/// beneath a predictable extra parent before ZIP/junction preflight.
/// </summary>
public sealed class SetupStagingDirectoryTests
{
    [Fact]
    public void CreatesDistinctImmediateTempChildrenWithoutLegacyParent()
    {
        var first = SetupStagingDirectory.Create();
        var second = SetupStagingDirectory.Create();
        try
        {
            Assert.NotEqual(first, second);
            var expectedParent = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.GetTempPath()));
            foreach (var path in new[] { first, second })
            {
                Assert.True(Directory.Exists(path));
                Assert.StartsWith(SetupStagingDirectory.Prefix,
                    Path.GetFileName(path));
                Assert.Equal(expectedParent,
                    Path.GetDirectoryName(path),
                    OperatingSystem.IsWindows() ?
                        StringComparer.OrdinalIgnoreCase :
                        StringComparer.Ordinal);
                Assert.DoesNotContain(
                    $"{Path.DirectorySeparatorChar}TheEasyWayForDrivers" +
                    $"{Path.DirectorySeparatorChar}", path,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void UniqueStagingStillSupportsLegitimateSetupZipExtraction()
    {
        using var payload = new MemoryStream();
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var path in new[] {
                "Service/App1.Service.exe",
                "Desktop/App2.Desktop.exe",
                "Service/runtime/hostfxr.dll"
            })
            {
                var entry = zip.CreateEntry(path);
                using var output = entry.Open();
                output.Write(Encoding.UTF8.GetBytes("synthetic"));
            }
        }
        payload.Position = 0;
        var staging = SetupStagingDirectory.Create();
        try
        {
            SetupPayloadArchive.Extract(payload, staging);
            Assert.Equal("synthetic", File.ReadAllText(Path.Combine(
                staging, "Service", "App1.Service.exe")));
            Assert.Equal("synthetic", File.ReadAllText(Path.Combine(
                staging, "Desktop", "App2.Desktop.exe")));
            Assert.True(File.Exists(Path.Combine(
                staging, "Service", "runtime", "hostfxr.dll")));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }
    
    [Fact]
    public void FirstPayloadWriteIsExclusiveAndDoesNotTruncateExistingFile()
    {
        var staging = SetupStagingDirectory.Create();
        try
        {
            using (var output = SetupStagingDirectory.CreatePayloadFile(staging))
                output.WriteByte(42);
            var zip = Path.Combine(staging, "payload.zip");
            Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(zip));
            Assert.Throws<IOException>(() =>
                SetupStagingDirectory.CreatePayloadFile(staging));
            Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(zip));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public void PreexistingLinkedAncestorFailsBeforePayloadIsWrittenOutsideRoot()
    {
        var scratch = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-StagingPrewrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var outside = Path.Combine(scratch, "outside");
        var link = Path.Combine(scratch, "redirect");
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(link, outside);
            var staging = Path.Combine(link, "missing-child");
            var error = Assert.Throws<InvalidDataException>(() =>
                SetupStagingDirectory.CreatePayloadFile(staging));
            Assert.Contains("collegamento", error.Message);
            Assert.DoesNotContain(scratch, error.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFileSystemEntries(outside));
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void MissingOrdinaryAncestorValidationHasNoSideEffects()
    {
        var scratch = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-StagingAbsent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var missing = Path.Combine(scratch, "one", "two");
            SetupStagingDirectory.EnsureNoReparseAncestors(missing);
            Assert.False(Directory.Exists(Path.Combine(scratch, "one")));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

}
