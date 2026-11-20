using System.Runtime.InteropServices;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #585: final staging cleanup is constrained to Temp and must fail closed
/// on late redirections, rather than recursively deleting arbitrary paths.
/// </summary>
public sealed class SetupStagingCleanupTests
{
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(
        string newFileName, string existingFileName,
        IntPtr securityAttributes);

    [Fact]
    public void ValidNestedStagingIsRemovedAndRepeatedCleanupIsSafe()
    {
        var staging = SetupStagingDirectory.Create();
        try
        {
            var nested = Path.Combine(staging, "Service", "runtime");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "host.dll"), "fixture");
            File.WriteAllText(Path.Combine(staging, "payload.zip"), "fixture");

            SetupStagingDirectory.DeleteIfExists(staging);
            Assert.False(Directory.Exists(staging));
            SetupStagingDirectory.DeleteIfExists(staging);
            Assert.False(Directory.Exists(staging));
        }
        finally
        {
            if (Directory.Exists(staging))
                SetupStagingDirectory.DeleteIfExists(staging);
        }
    }

    [Fact]
    public void CleanupRejectsNonStagingAndNestedPaths()
    {
        var other = Directory.CreateTempSubdirectory("OmegaDrive-Other-").FullName;
        var staging = SetupStagingDirectory.Create();
        try
        {
            var nested = Path.Combine(staging, "child");
            Directory.CreateDirectory(nested);
            var canary = Path.Combine(other, "keep.txt");
            File.WriteAllText(canary, "untouched");

            Assert.Throws<InvalidDataException>(() =>
                SetupStagingDirectory.DeleteIfExists(other));
            Assert.Throws<InvalidDataException>(() =>
                SetupStagingDirectory.DeleteIfExists(nested));
            Assert.Throws<InvalidDataException>(() =>
                SetupStagingDirectory.DeleteIfExists(Path.GetTempPath()));
            Assert.Equal("untouched", File.ReadAllText(canary));
            Assert.True(Directory.Exists(nested));
        }
        finally
        {
            SetupStagingDirectory.DeleteIfExists(staging);
            Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void WindowsLinkedChildAbortsCleanupBeforeAnyFileRemoval()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        var outside = Directory.CreateTempSubdirectory("OmegaDrive-CleanupCanary-").FullName;
        var linked = Path.Combine(staging, "redirect");
        try
        {
            var canary = Path.Combine(outside, "private.sys");
            File.WriteAllText(canary, "outside");
            var existing = Path.Combine(staging, "keep.exe");
            File.WriteAllText(existing, "inside");
            Directory.CreateSymbolicLink(linked, outside);

            Assert.Throws<InvalidDataException>(() =>
                SetupStagingDirectory.DeleteIfExists(staging));
            Assert.Equal("outside", File.ReadAllText(canary));
            Assert.Equal("inside", File.ReadAllText(existing));
        }
        finally
        {
            if (Directory.Exists(linked) &&
                (File.GetAttributes(linked) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(linked);
            SetupStagingDirectory.DeleteIfExists(staging);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void WindowsInjectedLateSymlinkIsRejectedByNativeCleanupWalker()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        var outside = Directory.CreateTempSubdirectory("OmegaDrive-LateCleanup-").FullName;
        var child = Path.Combine(staging, "nested");
        try
        {
            Directory.CreateDirectory(child);
            var keep = Path.Combine(staging, "keep.exe");
            var canary = Path.Combine(outside, "private.sys");
            File.WriteAllText(keep, "installed");
            File.WriteAllText(canary, "outside");

            var injected = false;
            var failure = Record.Exception(() =>
                SetupStagingDirectory.DeleteIfExistsWithBeforeDeletionForTests(
                    staging, () =>
                    {
                        Directory.Delete(child);
                        Directory.CreateSymbolicLink(child, outside);
                        injected = true;
                    }));

            Assert.True(injected);
            Assert.True(failure is InvalidDataException or IOException);
            Assert.Equal("installed", File.ReadAllText(keep));
            Assert.Equal("outside", File.ReadAllText(canary));
            Assert.Empty(Directory.GetFiles(staging, ".OmegaDrive-Anchor-*.lock"));
        }
        finally
        {
            if (Directory.Exists(child) &&
                (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(child);
            SetupStagingDirectory.DeleteIfExists(staging);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void WindowsHardlinkCannotDeleteExternalFileDuringCleanup()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        var external = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-CleanupHardlink-" + Guid.NewGuid().ToString("N") + ".sys");
        var linked = Path.Combine(staging, "driver.sys");
        File.WriteAllText(external, "outside");
        try
        {
            if (!CreateHardLinkW(linked, external, IntPtr.Zero))
                throw new IOException("Hardlink fixture failed: " +
                    Marshal.GetLastWin32Error());

            Assert.Throws<InvalidDataException>(() =>
                SetupStagingDirectory.DeleteIfExists(staging));
            Assert.Equal("outside", File.ReadAllText(external));
            Assert.True(File.Exists(linked));
        }
        finally
        {
            if (File.Exists(linked))
                File.Delete(linked);
            SetupStagingDirectory.DeleteIfExists(staging);
            File.Delete(external);
        }
    }
}
