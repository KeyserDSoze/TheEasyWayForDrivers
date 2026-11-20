using System.Runtime.InteropServices;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #576: installer/rollback tree copies must not touch the previous
/// destination or outside targets when an existing link is discovered.
/// Only disposable test directories are involved; no installer is run.
/// </summary>
public sealed class SetupTreeCopyGuardTests
{
    // File.CreateHardLink is not in the net10.0 reference assembly.
    // Use the native Win32 primitive in Windows-only test fixtures.
    private static void CreateHardLink(string path, string existingPath)
    {
        if (!CreateHardLinkW(path, existingPath, IntPtr.Zero))
            throw new IOException(
                "Impossibile creare l'hardlink del test (Win32 " +
                Marshal.GetLastWin32Error() + ").");
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(
        string newFileName, string existingFileName,
        IntPtr securityAttributes);

    private sealed class Scratch : IDisposable
    {
        private readonly List<string> _links = [];
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-TreeCopy-" + Guid.NewGuid().ToString("N"));

        public Scratch() => Directory.CreateDirectory(Root);

        public string At(params string[] components) =>
            Path.Combine([Root, .. components]);

        public void LinkFile(string link, string target)
        {
            File.CreateSymbolicLink(link, target);
            _links.Add(link);
        }

        public void LinkDirectory(string link, string target)
        {
            Directory.CreateSymbolicLink(link, target);
            _links.Add(link);
        }

        public void Dispose()
        {
            // Remove links themselves before recursive scratch cleanup; no
            // outside target may be traversed by the test fixture.
            foreach (var link in _links)
            {
                if (!Path.Exists(link))
                    continue;
                if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0)
                    continue;
                if (Directory.Exists(link))
                    Directory.Delete(link);
                else
                    File.Delete(link);
            }

            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public void ValidNestedInstallReplaceAndRollbackCopyStillWork()
    {
        using var scratch = new Scratch();
        var source = scratch.At("source");
        var installed = scratch.At("installed");
        var snapshot = scratch.At("rollback");
        Directory.CreateDirectory(Path.Combine(source, "runtime", "nested"));
        Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(source, "Service.exe"), "new");
        File.WriteAllText(Path.Combine(source, "runtime", "nested",
            "driver.dll"), "new driver");
        File.WriteAllText(Path.Combine(installed, "obsolete.txt"), "old");

        SetupTreeCopyGuard.ReplaceDirectory(source, installed);
        Assert.False(File.Exists(Path.Combine(installed, "obsolete.txt")));
        Assert.Equal("new", File.ReadAllText(
            Path.Combine(installed, "Service.exe")));
        Assert.Equal("new driver", File.ReadAllText(Path.Combine(
            installed, "runtime", "nested", "driver.dll")));

        SetupTreeCopyGuard.CopyDirectoryIfExists(installed, snapshot);
        Assert.Equal("new driver", File.ReadAllText(Path.Combine(
            snapshot, "runtime", "nested", "driver.dll")));

        // Restore a valid snapshot into an existing destination, preserving
        // normal overwrite behavior needed by the rollback Updater path.
        File.WriteAllText(Path.Combine(installed, "Service.exe"), "modified");
        SetupTreeCopyGuard.CopyDirectoryIfExists(snapshot, installed);
        Assert.Equal("new", File.ReadAllText(
            Path.Combine(installed, "Service.exe")));
    }

    [Fact]
    public void MissingInstallSourceDoesNotDeletePreviousInstall()
    {
        using var scratch = new Scratch();
        var installed = scratch.At("installed");
        Directory.CreateDirectory(installed);
        var old = Path.Combine(installed, "Service.exe");
        File.WriteAllText(old, "previous installation");

        Assert.ThrowsAny<IOException>(() =>
            SetupTreeCopyGuard.ReplaceDirectory(
                scratch.At("missing"), installed));
        Assert.Equal("previous installation", File.ReadAllText(old));
    }

    [Fact]
    public void OverlappingSourceAndDestinationFailBeforeDeletingFiles()
    {
        using var scratch = new Scratch();
        var folder = scratch.At("installed");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "keep.exe"), "old");

        Assert.Throws<InvalidDataException>(() =>
            SetupTreeCopyGuard.ReplaceDirectory(folder, folder));
        Assert.Throws<InvalidDataException>(() =>
            SetupTreeCopyGuard.ReplaceDirectory(folder,
                Path.Combine(folder, "child")));
        Assert.Equal("old", File.ReadAllText(
            Path.Combine(folder, "keep.exe")));
    }

    [Fact]
    public void MissingOptionalRollbackSourcePreservesDestination()
    {
        using var scratch = new Scratch();
        var existing = scratch.At("installed");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "keep.exe"), "old");

        SetupTreeCopyGuard.CopyDirectoryIfExists(
            scratch.At("no-updater"), existing);

        Assert.Equal("old", File.ReadAllText(
            Path.Combine(existing, "keep.exe")));
    }

    [Fact]
    public void SourceFileSymlinkIsRejectedBeforePreviousInstallDeletion()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source");
        var dest = scratch.At("installed");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);
        var old = Path.Combine(dest, "previous.exe");
        var outside = scratch.At("outside-canary");
        File.WriteAllText(old, "installed version");
        File.WriteAllText(outside, "private");
        scratch.LinkFile(Path.Combine(source, "driver.dll"), outside);

        Assert.Throws<InvalidDataException>(() =>
            SetupTreeCopyGuard.ReplaceDirectory(source, dest));

        Assert.Equal("installed version", File.ReadAllText(old));
        Assert.Equal("private", File.ReadAllText(outside));
        Assert.False(File.Exists(Path.Combine(dest, "driver.dll")));
    }

    [Fact]
    public void SourceDirectorySymlinkIsRejectedWithoutReadingOutsideTarget()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source");
        var dest = scratch.At("installed");
        var outside = scratch.At("outside");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(dest, "previous.exe"), "old");
        File.WriteAllText(Path.Combine(outside, "private.sys"), "secret");
        scratch.LinkDirectory(Path.Combine(source, "runtime"), outside);

        Assert.Throws<InvalidDataException>(() =>
            SetupTreeCopyGuard.ReplaceDirectory(source, dest));
        Assert.Equal("old", File.ReadAllText(
            Path.Combine(dest, "previous.exe")));
        Assert.Equal("secret", File.ReadAllText(
            Path.Combine(outside, "private.sys")));
        Assert.False(Directory.Exists(Path.Combine(dest, "runtime")));
    }

    [Fact]
    public void DestinationJunctionOrSymlinkIsRejectedBeforeDeletion()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source");
        var dest = scratch.At("installed");
        var outside = scratch.At("outside");
        Directory.CreateDirectory(Path.Combine(source, "runtime"));
        Directory.CreateDirectory(dest);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(source, "runtime", "driver.dll"), "new");
        File.WriteAllText(Path.Combine(dest, "previous.exe"), "old");
        File.WriteAllText(Path.Combine(outside, "canary.txt"), "canary");
        scratch.LinkDirectory(Path.Combine(dest, "runtime"), outside);

        Assert.Throws<InvalidDataException>(() =>
            SetupTreeCopyGuard.ReplaceDirectory(source, dest));

        Assert.Equal("old", File.ReadAllText(
            Path.Combine(dest, "previous.exe")));
        Assert.Equal("canary", File.ReadAllText(
            Path.Combine(outside, "canary.txt")));
        Assert.False(File.Exists(Path.Combine(outside, "driver.dll")));
    }

    [Fact]
    public void UpdaterFileSymlinkDestinationCannotOverwriteOutsideCanary()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("setup.exe");
        var outside = scratch.At("outside-canary");
        var destination = scratch.At("updater.exe");
        File.WriteAllText(source, "new setup");
        File.WriteAllText(outside, "private setup");
        scratch.LinkFile(destination, outside);

        Assert.Throws<InvalidDataException>(() =>
            SetupTreeCopyGuard.CopySingleFile(source, destination,
                overwrite: true));

        Assert.Equal("private setup", File.ReadAllText(outside));
        Assert.Equal("new setup", File.ReadAllText(source));
    }

    [Fact]
    public void WindowsHardlinkedSourceFileIsRejectedBeforeInstallDeletion()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source");
        var installed = scratch.At("installed");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(installed);
        var canary = scratch.At("outside-canary.txt");
        var old = Path.Combine(installed, "previous.exe");
        File.WriteAllText(canary, "outside contents");
        File.WriteAllText(old, "previous installation");
        CreateHardLink(Path.Combine(source, "driver.dll"), canary);

        Assert.Throws<InvalidDataException>(() =>
            SetupTreeCopyGuard.ReplaceDirectory(source, installed));

        Assert.Equal("previous installation", File.ReadAllText(old));
        Assert.Equal("outside contents", File.ReadAllText(canary));
        Assert.False(File.Exists(Path.Combine(installed, "driver.dll")));
    }

    [Fact]
    public void WindowsHardlinkedDestinationFileCannotBeReplaced()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source");
        var installed = scratch.At("installed");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(installed);
        var canary = scratch.At("outside-canary.txt");
        var old = Path.Combine(installed, "previous.exe");
        var shared = Path.Combine(installed, "driver.dll");
        File.WriteAllText(canary, "outside unchanged");
        File.WriteAllText(old, "existing install");
        File.WriteAllText(Path.Combine(source, "driver.dll"), "new driver");
        CreateHardLink(shared, canary);

        Assert.Throws<InvalidDataException>(() =>
            SetupTreeCopyGuard.ReplaceDirectory(source, installed));

        Assert.Equal("existing install", File.ReadAllText(old));
        Assert.Equal("outside unchanged", File.ReadAllText(canary));
        Assert.Equal("outside unchanged", File.ReadAllText(shared));
    }

    [Fact]
    public void WindowsRollbackCopyDoesNotOverwriteHardlinkedTarget()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var snapshot = scratch.At("rollback");
        var installed = scratch.At("installed");
        Directory.CreateDirectory(snapshot);
        Directory.CreateDirectory(installed);
        var canary = scratch.At("outside-canary.txt");
        File.WriteAllText(canary, "rollback canary");
        File.WriteAllText(Path.Combine(snapshot, "driver.sys"), "restored data");
        CreateHardLink(Path.Combine(installed, "driver.sys"), canary);

        Assert.Throws<InvalidDataException>(() =>
            SetupTreeCopyGuard.CopyDirectoryIfExists(snapshot, installed));

        Assert.Equal("rollback canary", File.ReadAllText(canary));
        Assert.Equal("rollback canary", File.ReadAllText(
            Path.Combine(installed, "driver.sys")));
    }

    [Fact]
    public void WindowsUpdaterCopyRejectsHardlinkedDestination()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("setup.exe");
        var canary = scratch.At("outside-canary.exe");
        var dest = scratch.At("Updater.exe");
        File.WriteAllText(source, "new setup");
        File.WriteAllText(canary, "previous protected executable");
        CreateHardLink(dest, canary);

        Assert.Throws<InvalidDataException>(() =>
            SetupTreeCopyGuard.CopySingleFile(source, dest,
                overwrite: true));

        Assert.Equal("previous protected executable",
            File.ReadAllText(canary));
        Assert.Equal("previous protected executable",
            File.ReadAllText(dest));
    }

    [Fact]
    public void WindowsUpdaterCopyRejectsHardlinkedSource()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var canary = scratch.At("outside-canary.exe");
        var source = scratch.At("setup.exe");
        var dest = scratch.At("Updater.exe");
        File.WriteAllText(canary, "outside executable");
        File.WriteAllText(dest, "old updater");
        CreateHardLink(source, canary);

        Assert.Throws<InvalidDataException>(() =>
            SetupTreeCopyGuard.CopySingleFile(source, dest,
                overwrite: true));

        Assert.Equal("old updater", File.ReadAllText(dest));
        Assert.Equal("outside executable", File.ReadAllText(canary));
    }

}
