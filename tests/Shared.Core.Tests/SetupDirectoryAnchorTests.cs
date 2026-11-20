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

    [Fact]
    public async Task ConcurrentRenameAttemptsCannotDisplacePinnedStagingRoot()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sandbox = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-AnchorRace-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(sandbox, "stage");
        var moved = Path.Combine(sandbox, "moved");
        Directory.CreateDirectory(stage);
        try
        {
            using (SetupDirectoryAnchor.Open(stage))
            {
                Assert.Single(Directory.GetFiles(
                    stage, ".OmegaDrive-Anchor-*.lock"));

                // Try the same swap concurrently from multiple tasks while
                // the verified root identity and native child lock are held.
                var attempts = await Task.WhenAll(Enumerable.Range(0, 24)
                    .Select(_ => Task.Run(() =>
                        Record.Exception(() => Directory.Move(stage, moved)))));
                Assert.All(attempts, error =>
                    Assert.True(error is IOException or UnauthorizedAccessException,
                        "A rename succeeded while staging was anchored."));
                Assert.True(Directory.Exists(stage));
                Assert.False(Directory.Exists(moved));
            }

            Assert.Empty(Directory.GetFiles(
                stage, ".OmegaDrive-Anchor-*.lock"));
            Directory.Move(stage, moved);
            Assert.True(Directory.Exists(moved));
        }
        finally
        {
            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void SymlinkAtRootCannotCreateLockInsideOutsideTarget()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sandbox = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-AnchorLeafLink-" + Guid.NewGuid().ToString("N"));
        var actual = Path.Combine(sandbox, "outside");
        var link = Path.Combine(sandbox, "stage");
        Directory.CreateDirectory(actual);
        try
        {
            Directory.CreateSymbolicLink(link, actual);
            var error = Record.Exception(() =>
            {
                using var anchor = SetupDirectoryAnchor.Open(link);
            });
            Assert.True(error is IOException or InvalidDataException);
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
    public void DeepExistingTraversalWithSpacesReleasesOnlyLeafLock()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sandbox = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-RootWalk-" + Guid.NewGuid().ToString("N"));
        var leaf = Path.Combine(sandbox, "parent with spaces",
            "sub folder", "stage");
        var moved = Path.Combine(Path.GetDirectoryName(leaf)!, "moved");
        Directory.CreateDirectory(leaf);
        try
        {
            using (SetupDirectoryAnchor.Open(leaf))
            {
                Assert.Single(Directory.GetFiles(
                    leaf, ".OmegaDrive-Anchor-*.lock"));
                var failure = Record.Exception(() =>
                    Directory.Move(leaf, moved));
                Assert.True(failure is IOException or UnauthorizedAccessException);
                Assert.False(Directory.Exists(moved));
            }

            Assert.Empty(Directory.GetFiles(sandbox,
                ".OmegaDrive-Anchor-*.lock", SearchOption.AllDirectories));
            Directory.Move(leaf, moved);
            Assert.True(Directory.Exists(moved));
        }
        finally
        {
            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void MissingIntermediateAncestorFailsWithoutCreatingAnything()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sandbox = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-MissingWalk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            var missing = Path.Combine(sandbox, "absent", "stage");
            Assert.ThrowsAny<IOException>(() =>
                SetupDirectoryAnchor.Open(missing));
            Assert.False(Directory.Exists(Path.Combine(sandbox, "absent")));
            Assert.Empty(Directory.GetFileSystemEntries(sandbox));
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void ExistingLinkedAncestorNeverReceivesAStagingLock()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sandbox = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-LinkedWalk-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(sandbox, "outside");
        var outsideLeaf = Path.Combine(outside, "nested", "stage");
        var redirect = Path.Combine(sandbox, "redirect");
        Directory.CreateDirectory(outsideLeaf);
        try
        {
            Directory.CreateSymbolicLink(redirect, outside);
            var failure = Record.Exception(() =>
            {
                using var anchor = SetupDirectoryAnchor.Open(
                    Path.Combine(redirect, "nested", "stage"));
            });
            Assert.True(failure is IOException or InvalidDataException);
            Assert.Empty(Directory.GetFiles(outside,
                ".OmegaDrive-Anchor-*.lock", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFileSystemEntries(outsideLeaf));
        }
        finally
        {
            if (Directory.Exists(redirect))
                Directory.Delete(redirect);
            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void UncStagingRootRejectedWithoutNetworkLookup()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Throws<InvalidDataException>(() =>
            SetupDirectoryAnchor.Open(
                @"\\localhost\C$\OmegaDrive-Untrusted-Staging"));
    }


    [Fact]
    public void ReplacingNotYetOpenedLeafWithSymlinkFailsClosed()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sandbox = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-AnchorDeterministicLink-" + Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(sandbox, "parent");
        var leaf = Path.Combine(parent, "stage");
        var outside = Path.Combine(sandbox, "outside");
        Directory.CreateDirectory(leaf);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "canary.txt"), "untouched");
        var injected = false;
        try
        {
            // Hook runs AFTER parent is verified, BEFORE its child is
            // opened. The substitute is guaranteed to be in place at the
            // exact boundary, unlike a timing-dependent background loop.
            var failure = Record.Exception(() =>
            {
                using var anchor = SetupDirectoryAnchor
                    .OpenWithValidationObserverForTests(leaf, component =>
                    {
                        if (component != "parent")
                            return;

                        Directory.Delete(leaf);
                        Directory.CreateSymbolicLink(leaf, outside);
                        injected = true;
                    });
            });

            Assert.True(injected);
            Assert.True(failure is IOException or InvalidDataException,
                "A substituted leaf was not rejected.");
            Assert.Equal("untouched",
                File.ReadAllText(Path.Combine(outside, "canary.txt")));
            Assert.Empty(Directory.GetFiles(outside,
                ".OmegaDrive-Anchor-*.lock"));
            Assert.Empty(Directory.GetFiles(parent,
                ".OmegaDrive-Anchor-*.lock", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(leaf) &&
                (File.GetAttributes(leaf) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(leaf);
            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void SwapOfAlreadyOpenedLeafBeforeLockIsRejectedOrPrevented()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sandbox = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-AnchorIdentitySwap-" + Guid.NewGuid().ToString("N"));
        var leaf = Path.Combine(sandbox, "stage");
        var moved = Path.Combine(sandbox, "original-moved");
        Directory.CreateDirectory(leaf);
        Exception? renameFailure = null;
        var swapped = false;
        var observed = false;
        try
        {
            var openFailure = Record.Exception(() =>
            {
                using var anchor = SetupDirectoryAnchor
                    .OpenWithValidationObserverForTests(leaf, component =>
                    {
                        if (component != "stage")
                            return;

                        observed = true;
                        renameFailure = Record.Exception(() =>
                            Directory.Move(leaf, moved));
                        if (renameFailure is not null)
                            return;

                        Directory.CreateDirectory(leaf);
                        swapped = true;
                    });
            });

            Assert.True(observed);
            if (swapped)
            {
                // A filesystem that permits rename of an open directory
                // must fail closed when the name is replaced before fence.
                Assert.True(openFailure is IOException or InvalidDataException,
                    "Swap succeeded but validation accepted the wrong leaf.");
                Assert.Empty(Directory.GetFiles(leaf,
                    ".OmegaDrive-Anchor-*.lock"));
                Assert.Empty(Directory.GetFiles(moved,
                    ".OmegaDrive-Anchor-*.lock"));
            }
            else
            {
                // Some filesystems veto the rename as soon as the handle
                // is open. That outcome is also secure for this boundary.
                Assert.True(renameFailure is IOException or UnauthorizedAccessException);
                Assert.Null(openFailure);
                Assert.Empty(Directory.GetFiles(leaf,
                    ".OmegaDrive-Anchor-*.lock"));
            }
        }
        finally
        {
            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, recursive: true);
        }
    }

}
