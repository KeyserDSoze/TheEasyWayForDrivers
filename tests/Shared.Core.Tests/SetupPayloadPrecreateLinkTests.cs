using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #528: reject existing staging junctions BEFORE CreateDirectory can
/// create folders through them. These tests never run an installer.
/// </summary>
public sealed class SetupPayloadPrecreateLinkTests
{
    private sealed class Scratch : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "OmegaSetup-LinkPreflight-" + Guid.NewGuid().ToString("N"));

        public Scratch() => Directory.CreateDirectory(Root);

        public string Subpath(params string[] parts) =>
            Path.Combine([Root, .. parts]);

        public void Dispose()
        {
            // Only unlink actual reparse-point test directories here.
            // New nested-extraction tests also have an ORDINARY
            // Service/runtime directory which must be cleaned recursively.
            foreach (var link in new[] { Subpath("redirect"),
                         Subpath("stage", "Service", "runtime") })
                if (Directory.Exists(link) &&
                    (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0)
                    Directory.Delete(link);
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private static MemoryStream Zip(params string[] extra)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var name in new[]
            {
                "Service/App1.Service.exe",
                "Desktop/App2.Desktop.exe"
            }.Concat(extra))
            {
                var entry = zip.CreateEntry(name);
                if (name.EndsWith('/'))
                    continue;
                using var output = entry.Open();
                output.Write(Encoding.UTF8.GetBytes("synthetic"));
            }
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void MissingDestinationUnderExistingLinkDoesNotCreateExternalFolder()
    {
        using var scratch = new Scratch();
        var outside = scratch.Subpath("outside");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(scratch.Subpath("redirect"), outside);
        var destination = scratch.Subpath("redirect", "new-stage");
        using var zip = Zip();

        var error = Assert.Throws<InvalidDataException>(() =>
            SetupPayloadArchive.Extract(zip, destination));

        Assert.Contains("collegamento", error.Message);
        Assert.DoesNotContain(scratch.Root, error.Message);
        Assert.False(Directory.Exists(Path.Combine(outside, "new-stage")));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }

    [Theory]
    [InlineData("Service/runtime/deeper/")]
    [InlineData("Service/runtime/deeper/driver.dll")]
    public void NestedLinkCannotCreateExternalDirectoryBeforeReparseCheck(
        string zipEntry)
    {
        using var scratch = new Scratch();
        var outside = scratch.Subpath("outside");
        var stage = scratch.Subpath("stage");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(scratch.Subpath("stage", "Service"));
        Directory.CreateSymbolicLink(
            scratch.Subpath("stage", "Service", "runtime"), outside);
        using var zip = Zip(zipEntry);

        var error = Assert.Throws<InvalidDataException>(() =>
            SetupPayloadArchive.Extract(zip, stage));

        Assert.Contains("collegamento", error.Message);
        Assert.False(Directory.Exists(Path.Combine(outside, "deeper")));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }

    [Fact]
    public void AbsentOrdinaryParentsStillMaterializeForLegitimatePayload()
    {
        using var scratch = new Scratch();
        using var zip = Zip("Service/runtime/deeper/driver.dll");
        var stage = scratch.Subpath("new-stage", "nested");

        SetupPayloadArchive.Extract(zip, stage);

        Assert.Equal("synthetic", File.ReadAllText(Path.Combine(
            stage, "Service", "runtime", "deeper", "driver.dll")));
        Assert.True(File.Exists(Path.Combine(
            stage, "Desktop", "App2.Desktop.exe")));
    }

    [Fact]
    public void NestedFileFailureReleasesAllTemporaryDirectoryPins()
    {
        using var scratch = new Scratch();
        var stage = scratch.Subpath("stage");
        var nested = scratch.Subpath("stage", "Service", "runtime", "deeper");
        Directory.CreateDirectory(nested);
        var existing = Path.Combine(nested, "driver.dll");
        File.WriteAllText(existing, "original");
        using var zip = Zip("Service/runtime/deeper/driver.dll");

        Assert.Throws<IOException>(() =>
            SetupPayloadArchive.Extract(zip, stage));
        Assert.Equal("original", File.ReadAllText(existing));
        if (OperatingSystem.IsWindows())
            Assert.Empty(Directory.GetFiles(stage,
                ".OmegaDrive-Anchor-*.lock", SearchOption.AllDirectories));
    }

    [Fact]
    public void DeepImplicitParentsExtractWithoutResidualPins()
    {
        using var scratch = new Scratch();
        var stage = scratch.Subpath("stage");
        using var zip = Zip("Service/runtime/a/b/c/deep.dll");

        SetupPayloadArchive.Extract(zip, stage);

        Assert.Equal("synthetic", File.ReadAllText(
            scratch.Subpath("stage", "Service", "runtime",
                "a", "b", "c", "deep.dll")));
        if (OperatingSystem.IsWindows())
            Assert.Empty(Directory.GetFiles(stage,
                ".OmegaDrive-Anchor-*.lock", SearchOption.AllDirectories));
    }


    [Fact]
    public void WindowsZipEntrySymlinkDoesNotOverwriteOutsideFile()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var stage = scratch.Subpath("stage");
        var parent = scratch.Subpath("stage", "Service", "runtime");
        var outside = scratch.Subpath("outside.bin");
        var link = Path.Combine(parent, "driver.dll");
        Directory.CreateDirectory(parent);
        File.WriteAllText(outside, "preserve outside");
        try
        {
            File.CreateSymbolicLink(link, outside);
            using var zip = Zip("Service/runtime/driver.dll");

            Assert.Throws<IOException>(() =>
                SetupPayloadArchive.Extract(zip, stage));

            Assert.Equal("preserve outside", File.ReadAllText(outside));
            Assert.True(File.Exists(link));
            Assert.Empty(Directory.GetFiles(stage,
                ".OmegaDrive-Anchor-*.lock", SearchOption.AllDirectories));
        }
        finally
        {
            if (File.Exists(link))
                File.Delete(link);
        }
    }


    [Fact]
    public void WindowsNestedExplicitAndImplicitDirectoriesLeaveNoRenameLocks()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var stage = scratch.Subpath("stage");
        using var zip = Zip("Service/runtime/explicit/deeper/",
            "Service/runtime/explicit/deeper/one.dll",
            "Desktop/implicit/a/b/two.dll");

        SetupPayloadArchive.Extract(zip, stage);

        Assert.Equal("synthetic", File.ReadAllText(
            Path.Combine(stage, "Service", "runtime", "explicit",
                "deeper", "one.dll")));
        Assert.Equal("synthetic", File.ReadAllText(
            Path.Combine(stage, "Desktop", "implicit", "a", "b", "two.dll")));
        Assert.Empty(Directory.GetFiles(stage,
            ".OmegaDrive-Anchor-*.lock", SearchOption.AllDirectories));
    }

    [Fact]
    public void WindowsExistingRegularFileCannotBecomeZipParent()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var stage = scratch.Subpath("stage");
        var service = scratch.Subpath("stage", "Service");
        Directory.CreateDirectory(service);
        var blocker = Path.Combine(service, "runtime");
        File.WriteAllText(blocker, "unchanged");
        using var zip = Zip("Service/runtime/deeper/file.dll");

        Assert.Throws<IOException>(() =>
            SetupPayloadArchive.Extract(zip, stage));

        Assert.Equal("unchanged", File.ReadAllText(blocker));
        Assert.Empty(Directory.GetFiles(stage,
            ".OmegaDrive-Anchor-*.lock", SearchOption.AllDirectories));
    }

}
