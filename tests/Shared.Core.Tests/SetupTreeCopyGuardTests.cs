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


    [Fact]
    public void WindowsLateHardlinkSwapCannotOverwriteOutsideCanary()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source.exe");
        var dest = scratch.At("Updater.exe");
        var outside = scratch.At("outside-canary.exe");
        File.WriteAllText(source, "new installer bytes");
        File.WriteAllText(dest, "previous updater");
        File.WriteAllText(outside, "external canary");

        var swappedAfterCopy = false;
        SetupTreeCopyGuard.CopySingleFileWithBeforePublishForTests(
            source, dest, overwrite: true, beforePublish: () =>
            {
                // The copy has ALREADY passed the hardlink preflight and
                // written the replacement bytes into a new file. Insert
                // a destination hardlink precisely before publication.
                File.Delete(dest);
                CreateHardLink(dest, outside);
                swappedAfterCopy = true;
            });

        Assert.True(swappedAfterCopy);
        Assert.Equal("new installer bytes", File.ReadAllText(dest));
        Assert.Equal("external canary", File.ReadAllText(outside));
        Assert.Empty(Directory.GetFiles(scratch.Root,
            ".OmegaDrive-Copy-*.tmp"));
    }

    [Fact]
    public void WindowsCopyDoesNotOverwriteWhenFlagIsFalse()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("new.exe");
        var destination = scratch.At("old.exe");
        File.WriteAllText(source, "new");
        File.WriteAllText(destination, "old");

        Assert.Throws<IOException>(() =>
            SetupTreeCopyGuard.CopySingleFile(source, destination,
                overwrite: false));
        Assert.Equal("old", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(scratch.Root,
            ".OmegaDrive-Copy-*.tmp"));
    }

    [Fact]
    public void WindowsCopyPublishesNewDestinationWithoutTemporaryFiles()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("new.exe");
        var destination = scratch.At("created.exe");
        File.WriteAllText(source, "new contents");

        SetupTreeCopyGuard.CopySingleFile(source, destination,
            overwrite: false);

        Assert.Equal("new contents", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(scratch.Root,
            ".OmegaDrive-Copy-*.tmp"));
    }

    [Fact]
    public void WindowsFailedPublishRemovesTemporaryFile()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("setup.exe");
        var destination = scratch.At("Updater.exe");
        File.WriteAllText(source, "new version");
        File.WriteAllText(destination, "old version");

        // Force the native rename to fail AFTER the replacement has been
        // fully copied: directories cannot be overwritten by a file.
        Assert.Throws<IOException>(() =>
            SetupTreeCopyGuard.CopySingleFileWithBeforePublishForTests(
                source, destination, overwrite: true,
                beforePublish: () =>
                {
                    File.Delete(destination);
                    Directory.CreateDirectory(destination);
                }));

        Assert.True(Directory.Exists(destination));
        Assert.Empty(Directory.GetFiles(scratch.Root,
            ".OmegaDrive-Copy-*.tmp"));
    }


    [Fact]
    public void WindowsDestinationParentCannotMoveUntilCopyIsPublished()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var from = scratch.At("new-updater.exe");
        var parent = scratch.At("destination");
        var moved = scratch.At("destination-moved");
        var to = Path.Combine(parent, "Updater.exe");
        File.WriteAllText(from, "new bytes");
        Directory.CreateDirectory(parent);
        File.WriteAllText(to, "old bytes");

        Exception? renameFailure = null;
        var reachedPublish = false;
        SetupTreeCopyGuard.CopySingleFileWithBeforePublishForTests(
            from, to, overwrite: true, beforePublish: () =>
            {
                reachedPublish = true;
                Assert.Single(Directory.GetFiles(parent,
                    ".OmegaDrive-Anchor-*.lock"));
                Assert.Single(Directory.GetFiles(parent,
                    ".OmegaDrive-Copy-*.tmp"));

                // A deterministic attempt in the exact window from the
                // end of copying to the final publish. The anchored leaf
                // must not be movable by Directory.Move at this point.
                renameFailure = Record.Exception(() =>
                    Directory.Move(parent, moved));
                Assert.True(
                    renameFailure is IOException or UnauthorizedAccessException,
                    "Destination parent moved while its rename fence was held.");
            });

        Assert.True(reachedPublish);
        Assert.Equal("new bytes", File.ReadAllText(to));
        Assert.False(Directory.Exists(moved));
        Assert.Empty(Directory.GetFiles(parent,
            ".OmegaDrive-Copy-*.tmp"));
        Assert.Empty(Directory.GetFiles(parent,
            ".OmegaDrive-Anchor-*.lock"));

        // The lock must be released when the copy returns.
        Directory.Move(parent, moved);
        Assert.Equal("new bytes", File.ReadAllText(
            Path.Combine(moved, "Updater.exe")));
    }

    [Fact]
    public void WindowsFailedPublishReleasesDestinationParentAnchor()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var from = scratch.At("setup.exe");
        var parent = scratch.At("destination");
        var moved = scratch.At("destination-moved");
        var to = Path.Combine(parent, "Updater.exe");
        Directory.CreateDirectory(parent);
        File.WriteAllText(from, "new setup");
        File.WriteAllText(to, "old setup");

        Assert.Throws<IOException>(() =>
            SetupTreeCopyGuard.CopySingleFileWithBeforePublishForTests(
                from, to, overwrite: true, beforePublish: () =>
                {
                    Assert.Single(Directory.GetFiles(parent,
                        ".OmegaDrive-Anchor-*.lock"));
                    File.Delete(to);
                    Directory.CreateDirectory(to);
                }));

        Assert.Empty(Directory.GetFiles(parent,
            ".OmegaDrive-Copy-*.tmp"));
        Assert.Empty(Directory.GetFiles(parent,
            ".OmegaDrive-Anchor-*.lock"));

        // Even after a native rename failure, nothing may pin the parent.
        Directory.Move(parent, moved);
        Assert.True(Directory.Exists(
            Path.Combine(moved, "Updater.exe")));
    }

    [Fact]
    public void WindowsCopyRefusesRedirectedDestinationParentWithoutOutsideWrites()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var from = scratch.At("source.exe");
        var outside = scratch.At("outside");
        var redirect = scratch.At("redirect");
        File.WriteAllText(from, "new installer");
        Directory.CreateDirectory(outside);
        var canary = Path.Combine(outside, "canary.exe");
        File.WriteAllText(canary, "untouched");
        scratch.LinkDirectory(redirect, outside);

        Assert.Throws<InvalidDataException>(() =>
            SetupTreeCopyGuard.CopySingleFile(
                from, Path.Combine(redirect, "Updater.exe"),
                overwrite: true));

        Assert.Equal("untouched", File.ReadAllText(canary));
        Assert.False(File.Exists(Path.Combine(outside, "Updater.exe")));
        Assert.Empty(Directory.GetFiles(outside,
            ".OmegaDrive-Copy-*.tmp"));
        Assert.Empty(Directory.GetFiles(outside,
            ".OmegaDrive-Anchor-*.lock"));
    }


    [Fact]
    public void WindowsTempNameCannotBeRemovedAfterCopyBeforeHandleRename()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source.exe");
        var parent = scratch.At("destination");
        Directory.CreateDirectory(parent);
        var destination = Path.Combine(parent, "Updater.exe");
        File.WriteAllText(source, "verified contents");
        File.WriteAllText(destination, "previous release");

        Exception? attempt = null;
        SetupTreeCopyGuard.CopySingleFileWithBeforePublishForTests(
            source, destination, overwrite: true,
            beforePublish: () =>
            {
                var temp = Assert.Single(Directory.GetFiles(parent,
                    ".OmegaDrive-Copy-*.tmp"));
                attempt = Record.Exception(() => File.Delete(temp));
                Assert.True(attempt is IOException or UnauthorizedAccessException,
                    "The temporary file was unlinked before handle rename.");
                Assert.True(File.Exists(temp));
                Assert.Single(Directory.GetFiles(parent,
                    ".OmegaDrive-Anchor-*.lock"));
            });

        Assert.NotNull(attempt);
        Assert.Equal("verified contents", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(parent,
            ".OmegaDrive-Copy-*.tmp"));
        Assert.Empty(Directory.GetFiles(parent,
            ".OmegaDrive-Anchor-*.lock"));
    }

    [Fact]
    public void WindowsHandleRenameReplacesLateSymlinkWithoutFollowingTarget()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source.exe");
        var parent = scratch.At("destination");
        Directory.CreateDirectory(parent);
        var destination = Path.Combine(parent, "Updater.exe");
        var outside = scratch.At("outside-canary.exe");
        File.WriteAllText(source, "new executable");
        File.WriteAllText(destination, "old executable");
        File.WriteAllText(outside, "untouched");

        // Hook executes after copy; inject a new symlink as the destination
        // name and confirm FileRenameInfo replaces the link's directory
        // entry without following the outside path.
        SetupTreeCopyGuard.CopySingleFileWithBeforePublishForTests(
            source, destination, overwrite: true,
            beforePublish: () =>
            {
                File.Delete(destination);
                scratch.LinkFile(destination, outside);
            });

        Assert.Equal("new executable", File.ReadAllText(destination));
        Assert.Equal("untouched", File.ReadAllText(outside));
        Assert.Empty(Directory.GetFiles(parent,
            ".OmegaDrive-Copy-*.tmp"));
        Assert.Empty(Directory.GetFiles(parent,
            ".OmegaDrive-Anchor-*.lock"));
    }


    [Fact]
    public void WindowsInjectedDirectorySymlinkAfterPreflightCannotDeleteOutsideTree()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source");
        var installed = scratch.At("installed");
        var nested = Path.Combine(installed, "runtime");
        var outside = scratch.At("outside");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(outside);
        var old = Path.Combine(installed, "previous.exe");
        var canary = Path.Combine(outside, "keep.txt");
        File.WriteAllText(Path.Combine(source, "new.exe"), "new");
        File.WriteAllText(old, "previous install");
        File.WriteAllText(canary, "external content");

        var injected = false;
        var failure = Record.Exception(() =>
            SetupTreeCopyGuard.ReplaceDirectoryWithBeforeDeletionForTests(
                source, installed, beforeDeletion: () =>
                {
                    // Runs AFTER the old installation passed preflight,
                    // but BEFORE the pinned deletion walker opens it.
                    Directory.Delete(nested);
                    scratch.LinkDirectory(nested, outside);
                    injected = true;
                }));

        Assert.True(injected);
        Assert.IsType<InvalidDataException>(failure);
        Assert.Equal("previous install", File.ReadAllText(old));
        Assert.Equal("external content", File.ReadAllText(canary));
        Assert.False(File.Exists(Path.Combine(installed, "new.exe")));
        Assert.Empty(Directory.GetFiles(installed,
            ".OmegaDrive-Anchor-*.lock", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.GetFiles(outside,
            ".OmegaDrive-Anchor-*.lock"));
    }

    [Fact]
    public void WindowsInjectedFileSymlinkAfterPreflightCannotDeleteOldInstall()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source");
        var installed = scratch.At("installed");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(installed);
        var previous = Path.Combine(installed, "previous.exe");
        var canary = scratch.At("outside-canary.exe");
        File.WriteAllText(Path.Combine(source, "new.exe"), "new");
        File.WriteAllText(previous, "previous install");
        File.WriteAllText(canary, "external content");

        var failure = Record.Exception(() =>
            SetupTreeCopyGuard.ReplaceDirectoryWithBeforeDeletionForTests(
                source, installed, beforeDeletion: () =>
                {
                    File.Delete(previous);
                    scratch.LinkFile(previous, canary);
                }));

        Assert.IsType<InvalidDataException>(failure);
        Assert.Equal("external content", File.ReadAllText(canary));
        Assert.True(File.Exists(previous));
        Assert.False(File.Exists(Path.Combine(installed, "new.exe")));
        Assert.Empty(Directory.GetFiles(installed,
            ".OmegaDrive-Anchor-*.lock"));
    }

    [Fact]
    public void WindowsNestedReplacementDeletesOldFilesWithoutAnchorResidues()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source");
        var installed = scratch.At("installed");
        Directory.CreateDirectory(Path.Combine(source, "drivers", "new"));
        Directory.CreateDirectory(Path.Combine(installed, "drivers", "old"));
        File.WriteAllText(Path.Combine(source, "drivers", "new",
            "driver.sys"), "fresh driver");
        File.WriteAllText(Path.Combine(installed, "drivers", "old",
            "obsolete.sys"), "old driver");

        SetupTreeCopyGuard.ReplaceDirectory(source, installed);

        Assert.Equal("fresh driver", File.ReadAllText(
            Path.Combine(installed, "drivers", "new", "driver.sys")));
        Assert.False(Directory.Exists(
            Path.Combine(installed, "drivers", "old")));
        Assert.Empty(Directory.GetFiles(installed,
            ".OmegaDrive-Anchor-*.lock", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(installed,
            ".OmegaDrive-Copy-*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void WindowsReplacedHardlinkAfterPreflightIsRefusedBeforeDeletion()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source");
        var installed = scratch.At("installed");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(installed);
        var existing = Path.Combine(installed, "old.sys");
        var canary = scratch.At("outside.sys");
        File.WriteAllText(Path.Combine(source, "new.sys"), "new driver");
        File.WriteAllText(existing, "old driver");
        File.WriteAllText(canary, "external");

        var failure = Record.Exception(() =>
            SetupTreeCopyGuard.ReplaceDirectoryWithBeforeDeletionForTests(
                source, installed, beforeDeletion: () =>
                {
                    File.Delete(existing);
                    CreateHardLink(existing, canary);
                }));

        Assert.IsType<InvalidDataException>(failure);
        Assert.Equal("external", File.ReadAllText(canary));
        Assert.Equal("external", File.ReadAllText(existing));
        Assert.False(File.Exists(Path.Combine(installed, "new.sys")));
        Assert.Empty(Directory.GetFiles(installed,
            ".OmegaDrive-Anchor-*.lock"));
    }


    [Fact]
    public void WindowsEmptyDirectoryDeleteByHandleRejectsDifferentFileIdentity()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var original = scratch.At("original");
        var substitute = scratch.At("substitute");
        Directory.CreateDirectory(original);
        Directory.CreateDirectory(substitute);

        using var parentAnchor = SetupDirectoryAnchor.Open(scratch.Root);
        WindowsHandleRelativeFile.DirectoryIdentity originalId;
        using (var originalAnchor = SetupDirectoryAnchor.Open(original))
        {
            originalId = WindowsHandleRelativeFile.ReadDirectoryIdentity(
                originalAnchor.LeafDirectoryHandle);
        }

        // A different empty directory is not the original filesystem
        // object even when it is opened under a perfectly valid parent.
        Assert.Throws<InvalidDataException>(() =>
            WindowsHandleRelativeFile.DeleteVerifiedEmptyDirectory(
                parentAnchor.LeafDirectoryHandle, "substitute", originalId));
        Assert.True(Directory.Exists(original));
        Assert.True(Directory.Exists(substitute));
    }

    [Fact]
    public void WindowsEmptyDirectoryDeleteByHandleRefusesNonemptyChild()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var child = scratch.At("installed");
        Directory.CreateDirectory(child);
        var canary = Path.Combine(child, "keep.sys");
        File.WriteAllText(canary, "previous install");

        using var parentAnchor = SetupDirectoryAnchor.Open(scratch.Root);
        WindowsHandleRelativeFile.DirectoryIdentity expected;
        using (var childAnchor = SetupDirectoryAnchor.Open(child))
        {
            expected = WindowsHandleRelativeFile.ReadDirectoryIdentity(
                childAnchor.LeafDirectoryHandle);
        }

        Assert.Throws<IOException>(() =>
            WindowsHandleRelativeFile.DeleteVerifiedEmptyDirectory(
                parentAnchor.LeafDirectoryHandle, "installed", expected));
        Assert.Equal("previous install", File.ReadAllText(canary));
        Assert.True(Directory.Exists(child));
    }

    [Fact]
    public void WindowsEmptyDirectoryDeleteByHandleRejectsSymlinkTarget()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var original = scratch.At("original");
        var outside = scratch.At("outside");
        var redirect = scratch.At("redirect");
        Directory.CreateDirectory(original);
        Directory.CreateDirectory(outside);
        var canary = Path.Combine(outside, "keep.txt");
        File.WriteAllText(canary, "external content");
        scratch.LinkDirectory(redirect, outside);

        using var parentAnchor = SetupDirectoryAnchor.Open(scratch.Root);
        WindowsHandleRelativeFile.DirectoryIdentity expected;
        using (var anchor = SetupDirectoryAnchor.Open(original))
            expected = WindowsHandleRelativeFile.ReadDirectoryIdentity(
                anchor.LeafDirectoryHandle);

        var failure = Record.Exception(() =>
            WindowsHandleRelativeFile.DeleteVerifiedEmptyDirectory(
                parentAnchor.LeafDirectoryHandle, "redirect", expected));
        Assert.True(failure is IOException or InvalidDataException);
        Assert.Equal("external content", File.ReadAllText(canary));
        Assert.True(Directory.Exists(original));
    }

    [Fact]
    public void WindowsLateReplacementOfEmptyDirectoryIsRejectedOrRenameVetoed()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source");
        var installed = scratch.At("installed");
        var moved = scratch.At("installed-moved");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(source, "new.exe"), "new");
        File.WriteAllText(Path.Combine(installed, "old.exe"), "old");

        var observed = false;
        var swapped = false;
        Exception? renameFailure = null;
        var failure = Record.Exception(() =>
            SetupTreeCopyGuard.ReplaceDirectoryWithBeforeEmptyDeleteForTests(
                source, installed, beforeEmptyDelete: path =>
                {
                    if (path != installed)
                        return;

                    observed = true;
                    // The leaf rename fence has just been released, but
                    // the verified parent stays pinned. If Windows allows
                    // moving its empty child, install a different object
                    // at the same name before the native deletion.
                    renameFailure = Record.Exception(() =>
                        Directory.Move(installed, moved));
                    if (renameFailure is not null)
                        return;
                    Directory.CreateDirectory(installed);
                    swapped = true;
                }));

        Assert.True(observed);
        if (swapped)
        {
            Assert.IsType<InvalidDataException>(failure);
            Assert.True(Directory.Exists(installed));
            Assert.True(Directory.Exists(moved));
            Assert.False(File.Exists(Path.Combine(installed, "new.exe")));
        }
        else
        {
            Assert.True(renameFailure is IOException or UnauthorizedAccessException);
            Assert.Null(failure);
            Assert.Equal("new",
                File.ReadAllText(Path.Combine(installed, "new.exe")));
        }

        Assert.Empty(Directory.GetFiles(scratch.Root,
            ".OmegaDrive-Anchor-*.lock", SearchOption.AllDirectories));
    }

    [Fact]
    public void WindowsNormalNestedReplacementLeavesNoDeletionAnchors()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var source = scratch.At("source");
        var installed = scratch.At("installed");
        Directory.CreateDirectory(Path.Combine(source, "new", "nested"));
        Directory.CreateDirectory(Path.Combine(installed, "old", "nested"));
        File.WriteAllText(Path.Combine(source, "new", "nested",
            "driver.sys"), "new driver");
        File.WriteAllText(Path.Combine(installed, "old", "nested",
            "old.sys"), "old driver");

        SetupTreeCopyGuard.ReplaceDirectory(source, installed);

        Assert.False(Directory.Exists(Path.Combine(installed, "old")));
        Assert.Equal("new driver", File.ReadAllText(
            Path.Combine(installed, "new", "nested", "driver.sys")));
        Assert.Empty(Directory.GetFiles(scratch.Root,
            ".OmegaDrive-Anchor-*.lock", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(scratch.Root,
            ".OmegaDrive-Copy-*.tmp", SearchOption.AllDirectories));
    }

}
