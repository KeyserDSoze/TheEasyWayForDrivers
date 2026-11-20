using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #594: uninstall cleanup must not accidentally use Temp-only staging
/// deletion. These tests operate only inside disposable scratch directories.
/// </summary>
public sealed class SetupUninstallCleanupTests
{
    [Fact]
    public void RemovesServiceDesktopAndProgramDataButNotUpdaterOrSiblings()
    {
        using var scratch = new Scratch();
        var install = scratch.At("ProgramFiles", "TheEasyWayForDrivers");
        var data = scratch.At("ProgramData", "TheEasyWayForDrivers");
        var service = Path.Combine(install, "Service");
        var desktop = Path.Combine(install, "Desktop");
        var updater = Path.Combine(install, "Updater");
        Directory.CreateDirectory(Path.Combine(service, "runtime", "nested"));
        Directory.CreateDirectory(desktop);
        Directory.CreateDirectory(updater);
        Directory.CreateDirectory(Path.Combine(data, "rollback", "Service"));
        File.WriteAllText(Path.Combine(service, "runtime", "nested", "Service.dll"),
            "service bytes");
        File.WriteAllText(Path.Combine(desktop, "Desktop.exe"), "desktop bytes");
        File.WriteAllText(Path.Combine(data, "rollback", "Service", "old.bin"),
            "rollback bytes");
        var updaterFile = Path.Combine(updater, "OmegaDrive-Setup.exe");
        var siblingFile = scratch.At("ProgramData", "other-app.txt");
        File.WriteAllText(updaterFile, "running setup placeholder");
        File.WriteAllText(siblingFile, "other application");

        SetupUninstallCleanup.DeleteApplicationTrees(install, data);

        Assert.False(Directory.Exists(service));
        Assert.False(Directory.Exists(desktop));
        Assert.False(Directory.Exists(data));
        Assert.Equal("running setup placeholder", File.ReadAllText(updaterFile));
        Assert.Equal("other application", File.ReadAllText(siblingFile));

        // Repeated cleanup of already-removed components must be safe.
        SetupUninstallCleanup.DeleteApplicationTrees(install, data);
        Assert.True(File.Exists(updaterFile));
    }

    [Fact]
    public void RejectsWrongRootNamesAndOverlappingRootsBeforeDeletingFiles()
    {
        using var scratch = new Scratch();
        var install = scratch.At("ProgramFiles", "TheEasyWayForDrivers");
        var data = scratch.At("ProgramData", "TheEasyWayForDrivers");
        var desktop = Path.Combine(install, "Desktop");
        Directory.CreateDirectory(desktop);
        var canary = Path.Combine(desktop, "keep.exe");
        File.WriteAllText(canary, "existing app");

        Assert.Throws<InvalidDataException>(() =>
            SetupUninstallCleanup.DeleteApplicationTrees(
                scratch.At("ProgramFiles"), data));
        Assert.Throws<InvalidDataException>(() =>
            SetupUninstallCleanup.DeleteApplicationTrees(
                install, scratch.At("ProgramData")));
        Assert.Throws<InvalidDataException>(() =>
            SetupUninstallCleanup.DeleteApplicationTrees(install, install));

        Assert.Equal("existing app", File.ReadAllText(canary));
    }

    [Fact]
    public void WindowsUninstallRejectsLinkedComponentWithoutDeletingOutside()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var scratch = new Scratch();
        var install = scratch.At("ProgramFiles", "TheEasyWayForDrivers");
        var data = scratch.At("ProgramData", "TheEasyWayForDrivers");
        var outside = scratch.At("outside");
        var service = Path.Combine(install, "Service");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(outside);
        var canary = Path.Combine(outside, "keep.sys");
        File.WriteAllText(canary, "outside data");
        var link = Path.Combine(service);
        Directory.CreateSymbolicLink(link, outside);
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                SetupUninstallCleanup.DeleteApplicationTrees(install, data));
            Assert.Equal("outside data", File.ReadAllText(canary));
            Assert.True(Directory.Exists(link));
            Assert.Empty(Directory.GetFiles(outside, ".OmegaDrive-Anchor-*.lock"));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    private sealed class Scratch : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-UninstallTest-" + Guid.NewGuid().ToString("N"));

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
