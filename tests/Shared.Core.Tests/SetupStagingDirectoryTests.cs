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
}
