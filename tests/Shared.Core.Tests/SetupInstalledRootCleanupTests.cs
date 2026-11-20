using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #596: no shell rmdir; delete only a validated, detached OmegaDrive root.
/// Uses disposable fixture directories; never touches a real installation.
/// </summary>
public sealed class SetupInstalledRootCleanupTests
{
    [Fact]
    public void DetachedUninstallRemovesNestedUpdaterAndPreservesOutsideCanary()
    {
        using var scratch = new Scratch();
        var programFiles = scratch.At("ProgramFiles");
        var install = Path.Combine(programFiles, "TheEasyWayForDrivers");
        var updater = Path.Combine(install, "Updater", "nested");
        var executable = scratch.At("Downloads", "OmegaDrive-Setup.exe");
        Directory.CreateDirectory(updater);
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(Path.Combine(updater, "remaining.exe"), "installed");
        File.WriteAllText(executable, "external setup");
        var canary = scratch.At("ProgramFiles", "other-product.txt");
        File.WriteAllText(canary, "untouched");

        Assert.Equal(InstallRootCleanupOutcome.Removed,
            SetupInstalledRootCleanup.Cleanup(install, programFiles, executable));
        Assert.False(Directory.Exists(install));
        Assert.Equal("untouched", File.ReadAllText(canary));
        Assert.Equal("external setup", File.ReadAllText(executable));
        Assert.Equal(InstallRootCleanupOutcome.AlreadyAbsent,
            SetupInstalledRootCleanup.Cleanup(install, programFiles, executable));
    }

    [Fact]
    public void RunningSetupInUpdaterReturnsPendingWithoutDeletingAnything()
    {
        using var scratch = new Scratch();
        var programFiles = scratch.At("ProgramFiles");
        var install = Path.Combine(programFiles, "TheEasyWayForDrivers");
        var updater = Path.Combine(install, "Updater");
        Directory.CreateDirectory(updater);
        var running = Path.Combine(updater, "OmegaDrive-Setup.exe");
        File.WriteAllText(running, "running setup");

        Assert.Equal(InstallRootCleanupOutcome.PendingRunningSetup,
            SetupInstalledRootCleanup.Cleanup(install, programFiles, running));
        Assert.Equal("running setup", File.ReadAllText(running));
        Assert.Equal(InstallRootCleanupOutcome.PendingRunningSetup,
            SetupInstalledRootCleanup.Cleanup(install, programFiles, null));
        Assert.Equal("running setup", File.ReadAllText(running));
        Assert.Empty(Directory.GetFiles(install,
            ".OmegaDrive-Anchor-*.lock", SearchOption.AllDirectories));
    }

    [Fact]
    public void WrongRootAndPartialPrefixAreRejectedBeforeDeletion()
    {
        using var scratch = new Scratch();
        var programFiles = scratch.At("ProgramFiles");
        var install = Path.Combine(programFiles, "TheEasyWayForDrivers");
        Directory.CreateDirectory(install);
        var canary = Path.Combine(install, "keep.txt");
        File.WriteAllText(canary, "keep");
        var externalSetup = scratch.At("Downloads", "setup.exe");

        Assert.Throws<InvalidDataException>(() =>
            SetupInstalledRootCleanup.Cleanup(programFiles, programFiles,
                externalSetup));
        Assert.Throws<InvalidDataException>(() =>
            SetupInstalledRootCleanup.Cleanup(
                scratch.At("Other", "TheEasyWayForDrivers"),
                programFiles, externalSetup));
        Assert.Throws<InvalidDataException>(() =>
            SetupInstalledRootCleanup.Cleanup(
                scratch.At("ProgramFiles", "TheEasyWayForDrivers-other"),
                programFiles, externalSetup));
        Assert.Equal("keep", File.ReadAllText(canary));
    }

    [Fact]
    public void RunningSetupInSimilarPrefixIsNotMistakenForInstalledExecutable()
    {
        using var scratch = new Scratch();
        var programFiles = scratch.At("ProgramFiles");
        var install = Path.Combine(programFiles, "TheEasyWayForDrivers");
        Directory.CreateDirectory(Path.Combine(install, "Updater"));
        File.WriteAllText(Path.Combine(install, "Updater", "setup.exe"), "old");
        var similar = scratch.At("ProgramFiles",
            "TheEasyWayForDrivers-old", "setup.exe");
        Assert.Equal(InstallRootCleanupOutcome.Removed,
            SetupInstalledRootCleanup.Cleanup(install, programFiles, similar));
        Assert.False(Directory.Exists(install));
    }

    [Fact]
    public void WindowsSymlinkedInstallationRootFailsClosed()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var scratch = new Scratch();
        var programFiles = scratch.At("ProgramFiles");
        var install = Path.Combine(programFiles, "TheEasyWayForDrivers");
        var outside = scratch.At("outside");
        Directory.CreateDirectory(programFiles);
        Directory.CreateDirectory(outside);
        var canary = Path.Combine(outside, "private.sys");
        File.WriteAllText(canary, "outside");
        Directory.CreateSymbolicLink(install, outside);
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                SetupInstalledRootCleanup.Cleanup(install, programFiles,
                    scratch.At("Downloads", "setup.exe")));
            Assert.Equal("outside", File.ReadAllText(canary));
        }
        finally
        {
            Directory.Delete(install);
        }
    }

    [Fact]
    public void WindowsLateSymlinkSwapFailsWithoutDeletingOutsideCanary()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var scratch = new Scratch();
        var programFiles = scratch.At("ProgramFiles");
        var install = Path.Combine(programFiles, "TheEasyWayForDrivers");
        var outside = scratch.At("outside");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(outside);
        var canary = Path.Combine(outside, "keep.sys");
        File.WriteAllText(canary, "outside");
        var injected = false;
        try
        {
            var failure = Record.Exception(() =>
                SetupInstalledRootCleanup.CleanupWithBeforeDeleteForTests(
                    install, programFiles, scratch.At("Downloads", "setup.exe"),
                    () =>
                    {
                        Directory.Delete(install);
                        Directory.CreateSymbolicLink(install, outside);
                        injected = true;
                    }));
            Assert.True(injected);
            Assert.True(failure is InvalidDataException or IOException);
            Assert.Equal("outside", File.ReadAllText(canary));
            Assert.False(File.Exists(Path.Combine(outside, "setup.exe")));
        }
        finally
        {
            if (Directory.Exists(install))
                Directory.Delete(install);
        }
    }

    [Fact]
    public void WindowsLockedUpdaterCannotBeReportedAsRemoved()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var scratch = new Scratch();
        var programFiles = scratch.At("ProgramFiles");
        var install = Path.Combine(programFiles, "TheEasyWayForDrivers");
        var updater = Path.Combine(install, "Updater");
        Directory.CreateDirectory(updater);
        var locked = Path.Combine(updater, "locked.exe");
        File.WriteAllText(locked, "locked file");
        using (var handle = File.Open(locked, FileMode.Open,
                   FileAccess.Read, FileShare.None))
        {
            var failure = Record.Exception(() =>
                SetupInstalledRootCleanup.Cleanup(install, programFiles,
                    scratch.At("Downloads", "setup.exe")));
            Assert.True(failure is IOException or UnauthorizedAccessException);
            Assert.True(File.Exists(locked));
        }
        // After the simulated running file is closed, cleanup can succeed.
        Assert.Equal(InstallRootCleanupOutcome.Removed,
            SetupInstalledRootCleanup.Cleanup(install, programFiles,
                scratch.At("Downloads", "setup.exe")));
        Assert.False(Directory.Exists(install));
    }

    private sealed class Scratch : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-RootCleanupTest-" + Guid.NewGuid().ToString("N"));

        public Scratch() => Directory.CreateDirectory(Root);

        public string At(params string[] parts) =>
            Path.Combine([Root, .. parts]);

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
