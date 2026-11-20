using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class SetupRollbackSnapshotCleanupTests
{
    [Fact]
    public void PriorSnapshotIsRemovedBeforeNewVersionCanBeRecorded()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-RollbackTest-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "ProgramData", "TheEasyWayForDrivers");
        var rollback = Path.Combine(data, "Rollback");
        var oldService = Path.Combine(rollback, "Service");
        try
        {
            Directory.CreateDirectory(oldService);
            var oldFile = Path.Combine(oldService, "stale.dll");
            File.WriteAllText(oldFile, "old snapshot");
            File.WriteAllText(Path.Combine(rollback, "version.txt"), "0.0.100");

            SetupRollbackSnapshotCleanup.DeletePreviousSnapshot(data);
            Assert.False(Directory.Exists(rollback));

            Directory.CreateDirectory(rollback);
            File.WriteAllText(Path.Combine(rollback, "version.txt"), "0.0.459");
            Assert.False(File.Exists(oldFile));
            Assert.Equal("0.0.459",
                File.ReadAllText(Path.Combine(rollback, "version.txt")));

            SetupRollbackSnapshotCleanup.DeletePreviousSnapshot(data);
            SetupRollbackSnapshotCleanup.DeletePreviousSnapshot(data);
            Assert.False(Directory.Exists(rollback));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WrongRootIsRejectedAndExternalCanaryPreserved()
    {
        var root = Directory.CreateTempSubdirectory(
            "OmegaDrive-RollbackScopeTest-").FullName;
        try
        {
            var rollback = Path.Combine(root, "Rollback");
            Directory.CreateDirectory(rollback);
            var canary = Path.Combine(rollback, "keep.txt");
            File.WriteAllText(canary, "untouched");

            Assert.Throws<InvalidDataException>(() =>
                SetupRollbackSnapshotCleanup.DeletePreviousSnapshot(root));
            Assert.Equal("untouched", File.ReadAllText(canary));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WindowsLinkedSnapshotFailsClosedWithoutReadingOutside()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-RollbackLink-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "ProgramData", "TheEasyWayForDrivers");
        var rollback = Path.Combine(data, "Rollback");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(outside);
        var canary = Path.Combine(outside, "keep.sys");
        File.WriteAllText(canary, "external data");
        Directory.CreateSymbolicLink(rollback, outside);
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                SetupRollbackSnapshotCleanup.DeletePreviousSnapshot(data));
            Assert.Equal("external data", File.ReadAllText(canary));
        }
        finally
        {
            Directory.Delete(rollback);
            Directory.Delete(root, recursive: true);
        }
    }
}
