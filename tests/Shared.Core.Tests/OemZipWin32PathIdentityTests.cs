using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #526: ZIP metadata and setup staging must not interpret Win32 device
/// names or normalized trailing-segment aliases as distinct driver files.
/// </summary>
public sealed class OemZipWin32PathIdentityTests
{
    private static MemoryStream Zip(params string[] names)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var name in names)
            {
                var entry = zip.CreateEntry(name);
                if (name.EndsWith('/') || name.EndsWith('\\'))
                    continue;
                using var output = entry.Open();
                output.Write(Encoding.UTF8.GetBytes("fixture"));
            }
        }
        stream.Position = 0;
        return stream;
    }

    [Theory]
    [InlineData("drivers/CON.inf")]
    [InlineData("Drivers/AuX.CAT")]
    [InlineData("NUL.sys")]
    [InlineData("vendor/PRN")]
    [InlineData("drivers/com1.inf")]
    [InlineData("drivers/LPT9.cat")]
    [InlineData("drivers/COM¹.inf")]
    [InlineData("drivers/LPT².sys")]
    [InlineData(@"drivers\com³.cat")]
    [InlineData("drivers/graphics.inf.")]
    [InlineData("drivers./graphics.inf")]
    [InlineData("drivers /graphics.inf")]
    [InlineData(@"drivers\sound.inf ")]
    [InlineData("drivers//")]
    [InlineData(@"drivers\\")]
    public void AmbiguousWindowsMemberCannotBePresentedAsOemEvidence(
        string suspect)
    {
        using var archive = Zip("normal/gpu.inf", suspect);
        var error = Assert.Throws<InvalidDataException>(() =>
            OemArchiveInspector.Inspect(archive));
        Assert.DoesNotContain(suspect, error.Message);
        Assert.False(error.Message.Contains("normal/gpu.inf",
            StringComparison.OrdinalIgnoreCase));

        archive.Position = 0;
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read,
            leaveOpen: true);
        Assert.Throws<InvalidDataException>(() =>
            OemArchiveInspector.CreateValidatedZipEntryIndex(zip));
    }

    [Fact]
    public void ValidWindowsNamesWithNestedDirectoriesStillWork()
    {
        using var archive = Zip(
            "drivers/NVIDIA/geforce.inf",
            @"drivers\Intel\killer.cat",
            "drivers/Realtek/audio.sys",
            "drivers/Device10/COM10.inf",
            "drivers/AUX2/cat.inf",
            "drivers/nvidia/");

        var report = OemArchiveInspector.Inspect(archive);
        Assert.Equal(5, report.Entries.Count);
        Assert.Contains(report.Entries, e =>
            e.Path == "drivers/Intel/killer.cat");
        Assert.False(report.IsInstallable);
        Assert.False(report.ContentsAuthenticated);
    }

    private sealed class Destination : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "Omega-Setup-ZipIdentity-" +
            Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    [Theory]
    [InlineData("Service/COM¹.dll")]
    [InlineData("Desktop/LPT².sys")]
    [InlineData("Service/com³")]
    [InlineData("Desktop/Lpt¹.cat")]
    [InlineData("Service/sub/COM².txt")]
    public void SetupRejectsUnicodeDosAliasesBeforeCreatingStaging(
        string suspect)
    {
        using var archive = Zip(
            "Service/App1.Service.exe",
            "Desktop/App2.Desktop.exe",
            suspect);
        using var target = new Destination();

        Assert.Throws<InvalidDataException>(() =>
            SetupPayloadArchive.Extract(archive, target.Path));
        Assert.False(Directory.Exists(target.Path));
    }

    [Fact]
    public void LegitimateSetupNamesStillExtractWithoutWindowsDependency()
    {
        using var archive = Zip(
            "Service/App1.Service.exe",
            "Desktop/App2.Desktop.exe",
            "Service/COM10.dll",
            "Desktop/LPT0.txt");
        using var target = new Destination();
        SetupPayloadArchive.Extract(archive, target.Path);
        Assert.True(File.Exists(System.IO.Path.Combine(target.Path,
            "Service", "COM10.dll")));
        Assert.True(File.Exists(System.IO.Path.Combine(target.Path,
            "Desktop", "LPT0.txt")));
    }
}
