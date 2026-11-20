using System.Runtime.InteropServices;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #587: install/update Updater directory and legacy alias must be
/// handled relative to pinned Windows directory objects.
/// </summary>
public sealed class SetupUpdaterCopyTests
{
    private sealed class Scratch : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-UpdaterGuard-" + Guid.NewGuid().ToString("N"));
        public Scratch() => Directory.CreateDirectory(Root);
        public string At(params string[] parts) => Path.Combine([Root, .. parts]);
        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(
        string linkName, string existing, IntPtr security);

    [Fact]
    public void ValidUpdaterCopyCreatesDirectoryAndReplacesLegacyAlias()
    {
        using var scratch = new Scratch();
        var root = scratch.At("installed");
        Directory.CreateDirectory(root);
        var source = scratch.At("new-setup.exe");
        var updater = Path.Combine(root, "Updater");
        Directory.CreateDirectory(updater);
        var destination = Path.Combine(updater, "OmegaDrive-Setup.exe");
        var legacy = Path.Combine(updater, "TheEasyWayForDrivers-Setup.exe");
        File.WriteAllText(source, "new installer");
        File.WriteAllText(destination, "old installer");
        File.WriteAllText(legacy, "legacy installer");

        SetupTreeCopyGuard.CopySetupUpdater(source, root);

        Assert.Equal("new installer", File.ReadAllText(destination));
        Assert.Equal("new installer", File.ReadAllText(source));
        Assert.False(File.Exists(legacy));
        Assert.Empty(Directory.GetFiles(root, ".OmegaDrive-Anchor-*.lock"));
        Assert.Empty(Directory.GetFiles(updater, ".OmegaDrive-Anchor-*.lock"));
        Assert.Empty(Directory.GetFiles(updater, ".OmegaDrive-Copy-*.tmp"));

        // Idempotent when an existing Updater contains the installed EXE.
        SetupTreeCopyGuard.CopySetupUpdater(destination, root);
        Assert.Equal("new installer", File.ReadAllText(destination));
    }

    [Fact]
    public void NewUpdaterDirectoryIsCreatedForFreshInstall()
    {
        using var scratch = new Scratch();
        var root = scratch.At("installed");
        Directory.CreateDirectory(root);
        var source = scratch.At("setup.exe");
        File.WriteAllText(source, "fresh");

        SetupTreeCopyGuard.CopySetupUpdater(source, root);

        Assert.Equal("fresh", File.ReadAllText(
            Path.Combine(root, "Updater", "OmegaDrive-Setup.exe")));
    }

    [Fact]
    public void WindowsPreexistingUpdaterLinkCannotRedirectInstaller()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var root = scratch.At("installed");
        var external = scratch.At("outside");
        var updater = Path.Combine(root, "Updater");
        var source = scratch.At("setup.exe");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(external);
        File.WriteAllText(source, "setup");
        var canary = Path.Combine(external, "keep.txt");
        File.WriteAllText(canary, "outside");
        Directory.CreateSymbolicLink(updater, external);

        try
        {
            var failure = Record.Exception(() =>
                SetupTreeCopyGuard.CopySetupUpdater(source, root));
            Assert.True(failure is InvalidDataException or IOException);
            Assert.Equal("outside", File.ReadAllText(canary));
            Assert.False(File.Exists(Path.Combine(
                external, "OmegaDrive-Setup.exe")));
        }
        finally
        {
            Directory.Delete(updater);
        }
    }

    [Fact]
    public void WindowsLegacyHardlinkIsRejectedWithoutChangingCanary()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var root = scratch.At("installed");
        var updater = Path.Combine(root, "Updater");
        Directory.CreateDirectory(updater);
        var source = scratch.At("setup.exe");
        var canary = scratch.At("outside.sys");
        var legacy = Path.Combine(updater, "TheEasyWayForDrivers-Setup.exe");
        File.WriteAllText(source, "new");
        File.WriteAllText(canary, "untouched");
        if (!CreateHardLinkW(legacy, canary, IntPtr.Zero))
            throw new IOException("Hardlink fixture failed (Win32 " +
                Marshal.GetLastWin32Error() + ").");

        var failure = Record.Exception(() =>
            SetupTreeCopyGuard.CopySetupUpdater(source, root));
        Assert.IsType<InvalidDataException>(failure);
        Assert.Equal("untouched", File.ReadAllText(canary));
        Assert.True(File.Exists(legacy));
        Assert.False(File.Exists(Path.Combine(updater, "OmegaDrive-Setup.exe")));
        Assert.Empty(Directory.GetFiles(updater, ".OmegaDrive-Anchor-*.lock"));
    }

    [Fact]
    public void WindowsLegacySymlinkCannotDeleteExternalTarget()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var root = scratch.At("installed");
        var updater = Path.Combine(root, "Updater");
        Directory.CreateDirectory(updater);
        var source = scratch.At("setup.exe");
        var outside = scratch.At("outside.exe");
        File.WriteAllText(source, "new");
        File.WriteAllText(outside, "untouched");
        var legacy = Path.Combine(updater, "TheEasyWayForDrivers-Setup.exe");
        File.CreateSymbolicLink(legacy, outside);

        try
        {
            var failure = Record.Exception(() =>
                SetupTreeCopyGuard.CopySetupUpdater(source, root));
            Assert.True(failure is InvalidDataException or IOException);
            Assert.Equal("untouched", File.ReadAllText(outside));
            Assert.False(File.Exists(Path.Combine(updater, "OmegaDrive-Setup.exe")));
        }
        finally
        {
            File.Delete(legacy);
        }
    }

    [Fact]
    public void WindowsInstalledRootAndUpdaterArePinnedDuringCopy()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var root = scratch.At("installed");
        var updater = Path.Combine(root, "Updater");
        var source = scratch.At("setup.exe");
        Directory.CreateDirectory(root);
        File.WriteAllText(source, "payload");
        var checkedLocks = false;

        SetupTreeCopyGuard.CopySetupUpdaterWithBeforeCopyForTests(
            source, root, pinnedUpdater =>
            {
                Assert.Equal(updater, pinnedUpdater);
                Assert.Single(Directory.GetFiles(root,
                    ".OmegaDrive-Anchor-*.lock"));
                Assert.Single(Directory.GetFiles(updater,
                    ".OmegaDrive-Anchor-*.lock"));
                var rootRename = Record.Exception(() =>
                    Directory.Move(root, scratch.At("installed-moved")));
                var updaterRename = Record.Exception(() =>
                    Directory.Move(updater, scratch.At("updater-moved")));
                Assert.True(rootRename is IOException or UnauthorizedAccessException);
                Assert.True(updaterRename is IOException or UnauthorizedAccessException);
                checkedLocks = true;
            });

        Assert.True(checkedLocks);
        Assert.Equal("payload", File.ReadAllText(
            Path.Combine(updater, "OmegaDrive-Setup.exe")));
        Assert.Empty(Directory.GetFiles(root, ".OmegaDrive-Anchor-*.lock"));
        Assert.Empty(Directory.GetFiles(updater, ".OmegaDrive-Anchor-*.lock"));
    }
    [Fact]
    public void WindowsDanglingLegacySymlinkIsNotTreatedAsAbsent()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var root = scratch.At("installed");
        var updater = Path.Combine(root, "Updater");
        Directory.CreateDirectory(updater);
        var source = scratch.At("setup.exe");
        File.WriteAllText(source, "new");

        // The link points to a missing target. File.Exists behavior for
        // dangling symlinks can vary by runtime/platform: never use it as
        // evidence of a genuinely absent native directory child (#588).
        var missingOutside = scratch.At("future-outside.exe");
        var legacy = Path.Combine(updater,
            "TheEasyWayForDrivers-Setup.exe");
        File.CreateSymbolicLink(legacy, missingOutside);

        try
        {
            Assert.True((File.GetAttributes(legacy) &
                FileAttributes.ReparsePoint) != 0);
            var failure = Record.Exception(() =>
                SetupTreeCopyGuard.CopySetupUpdater(source, root));
            Assert.True(failure is InvalidDataException or IOException);
            Assert.False(File.Exists(Path.Combine(
                updater, "OmegaDrive-Setup.exe")));
            Assert.False(File.Exists(missingOutside));
            Assert.True((File.GetAttributes(legacy) &
                FileAttributes.ReparsePoint) != 0);
        }
        finally
        {
            File.Delete(legacy);
        }

        Assert.Empty(Directory.GetFiles(updater,
            ".OmegaDrive-Anchor-*.lock"));
    }

    [Fact]
    public void WindowsAliasInsertedAfterPinnedDirectoryIsRejected()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var root = scratch.At("installed");
        var updater = Path.Combine(root, "Updater");
        Directory.CreateDirectory(updater);
        var source = scratch.At("setup.exe");
        File.WriteAllText(source, "new");
        var external = scratch.At("canary.txt");
        File.WriteAllText(external, "unchanged");
        var legacy = Path.Combine(updater,
            "TheEasyWayForDrivers-Setup.exe");
        var inserted = false;
        try
        {
            var failure = Record.Exception(() =>
                SetupTreeCopyGuard.CopySetupUpdaterWithBeforeCopyForTests(
                    source, root, _ =>
                    {
                        // Deterministically insert the redirect after both
                        // root and Updater have been pinned, before native open.
                        File.CreateSymbolicLink(legacy, external);
                        inserted = true;
                    }));
            Assert.True(inserted);
            Assert.True(failure is InvalidDataException or IOException);
            Assert.Equal("unchanged", File.ReadAllText(external));
            Assert.False(File.Exists(Path.Combine(
                updater, "OmegaDrive-Setup.exe")));
        }
        finally
        {
            if (File.Exists(legacy))
                File.Delete(legacy);
        }

        Assert.Empty(Directory.GetFiles(root,
            ".OmegaDrive-Anchor-*.lock"));
        Assert.Empty(Directory.GetFiles(updater,
            ".OmegaDrive-Anchor-*.lock"));
    }

}
