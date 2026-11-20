using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #586: the protected staging leaf is created and audited relative to the
/// same already-verified Windows Temp directory handle.
/// </summary>
public sealed class WindowsPrivateStagingAtomicCreationTests
{
    [Fact]
    public void WindowsNativeCreationPreservesOwnerDaclAndScopedLocation()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var temp = Directory.CreateTempSubdirectory(
            "OmegaDrive-NativeStageParent-").FullName;
        string? staging = null;
        try
        {
            staging = WindowsPrivateStagingDirectory.Create(
                temp, SetupStagingDirectory.Prefix);
            Assert.Equal(temp, Path.GetDirectoryName(staging),
                StringComparer.OrdinalIgnoreCase);
            Assert.StartsWith(SetupStagingDirectory.Prefix,
                Path.GetFileName(staging));
            WindowsPrivateStagingDirectory.Verify(staging);
            using var anchor = SetupDirectoryAnchor.Open(staging);
            SetupStagingDirectory.VerifyPrivateWindowsRoot(anchor);
            Assert.Single(Directory.GetFiles(staging,
                ".OmegaDrive-Anchor-*.lock"));
        }
        finally
        {
            if (staging is not null && Directory.Exists(staging))
                Directory.Delete(staging);
            Directory.Delete(temp);
        }
    }

    [Fact]
    public void WindowsPrivateCreationKeepsNewLeafNamePinnedUntilAclAudit()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var temp = Directory.CreateTempSubdirectory(
            "OmegaDrive-NativeStageMove-").FullName;
        string? staging = null;
        Exception? renameError = null;
        try
        {
            staging = WindowsPrivateStagingDirectory.CreateWithAfterCreatedForTests(
                temp, SetupStagingDirectory.Prefix, created =>
                {
                    renameError = Record.Exception(() =>
                        Directory.Move(created, created + "-moved"));
                    Assert.True(renameError is IOException or UnauthorizedAccessException,
                        "The newly-created leaf was movable before ACL audit.");
                    Assert.False(Directory.Exists(created + "-moved"));
                    WindowsPrivateStagingDirectory.Verify(created);
                });
            Assert.NotNull(renameError);
            Assert.True(Directory.Exists(staging));
            WindowsPrivateStagingDirectory.Verify(staging);
        }
        finally
        {
            if (staging is not null && Directory.Exists(staging))
                Directory.Delete(staging);
            Directory.Delete(temp);
        }
    }

    [Fact]
    public void WindowsFailedAclAuditObserverDeletesOnlyNativeLeaf()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var temp = Directory.CreateTempSubdirectory(
            "OmegaDrive-NativeStageFailure-").FullName;
        var canary = Path.Combine(temp, "keep.txt");
        File.WriteAllText(canary, "untouched");
        string? created = null;
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                WindowsPrivateStagingDirectory.CreateWithAfterCreatedForTests(
                    temp, SetupStagingDirectory.Prefix, path =>
                    {
                        created = path;
                        throw new InvalidOperationException("fixture failure");
                    }));
            Assert.Equal("fixture failure", exception.Message);
            Assert.NotNull(created);
            Assert.False(Directory.Exists(created));
            Assert.Equal("untouched", File.ReadAllText(canary));
            Assert.Empty(Directory.GetFiles(temp,
                ".OmegaDrive-Anchor-*.lock"));
        }
        finally
        {
            if (created is not null && Directory.Exists(created))
                Directory.Delete(created);
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void WindowsPrivateCreationDoesNotFollowLinkedParent()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Directory.CreateTempSubdirectory(
            "OmegaDrive-NativeStageLink-").FullName;
        var outside = Path.Combine(root, "outside");
        var redirect = Path.Combine(root, "redirect");
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(redirect, outside);
            Assert.Throws<InvalidDataException>(() =>
                WindowsPrivateStagingDirectory.Create(
                    redirect, SetupStagingDirectory.Prefix));
            Assert.Empty(Directory.GetFileSystemEntries(outside));
        }
        finally
        {
            if (Directory.Exists(redirect))
                Directory.Delete(redirect);
            Directory.Delete(root, recursive: true);
        }
    }
}
