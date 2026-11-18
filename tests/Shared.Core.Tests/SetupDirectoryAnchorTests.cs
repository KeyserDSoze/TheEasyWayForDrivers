using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #562: Windows handle pins must prevent a directory from being renamed
/// while the privileged installer is using it.
/// These tests only manipulate disposable temporary directories.
/// </summary>
public sealed class SetupDirectoryAnchorTests
{
    [Fact]
    public void StagingRenameIsBlockedUntilAnchorIsDisposedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sandbox = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-Anchor-" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(sandbox, "stage");
        var moved = Path.Combine(sandbox, "moved");
        Directory.CreateDirectory(staging);
        try
        {
            using (SetupDirectoryAnchor.Open(staging))
            {
                Assert.Single(Directory.GetFiles(
                    staging, ".OmegaDrive-Anchor-*.lock"));
                var error = Record.Exception(() =>
                    Directory.Move(staging, moved));
                Assert.True(error is IOException or UnauthorizedAccessException);
                Assert.True(Directory.Exists(staging));
                Assert.False(Directory.Exists(moved));
                using var output = new FileStream(
                    Path.Combine(staging, "payload.zip"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None);
                output.WriteByte(1);
            }

            Assert.Empty(Directory.GetFiles(
                staging, ".OmegaDrive-Anchor-*.lock"));
            Directory.Move(staging, moved);
            Assert.True(File.Exists(Path.Combine(moved, "payload.zip")));
        }
        finally
        {
            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void ReparsePointInAncestorFailsClosed()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sandbox = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-AnchorLink-" + Guid.NewGuid().ToString("N"));
        var actual = Path.Combine(sandbox, "actual");
        var link = Path.Combine(sandbox, "redirect");
        Directory.CreateDirectory(actual);
        try
        {
            Directory.CreateSymbolicLink(link, actual);
            var failure = Record.Exception(() =>
            {
                using var anchor = SetupDirectoryAnchor.Open(
                    Path.Combine(link, "missing"));
            });
            Assert.True(failure is IOException or InvalidDataException);
            Assert.Empty(Directory.GetFileSystemEntries(actual));
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void MissingPathFailsClosedWithoutCreatingIt()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sandbox = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-AnchorMissing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            var missing = Path.Combine(sandbox, "missing");
            Assert.ThrowsAny<IOException>(() =>
                SetupDirectoryAnchor.Open(missing));
            Assert.False(Directory.Exists(missing));
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }
}
