using System.Diagnostics;
using System.Globalization;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #598: post-exit worker protocol, PID/start time identity, ACL-protected
/// staging status and guarded cleanup without invoking the actual installer.
/// </summary>
public sealed class SetupPostExitCleanupTests
{
    [Fact]
    public void RequestRoundTripRejectsMalformedOrExtraArguments()
    {
        var identity = new InstalledRootIdentity(1234, 7, 999);
        var request = new SetupFinalizerRequest(
            4321, 638990000000000000L, identity);
        var args = SetupPostExitCleanup.BuildArguments(request);

        Assert.True(SetupPostExitCleanup.TryParseArguments(
            args, out var parsed));
        Assert.Equal(request, parsed);
        Assert.False(SetupPostExitCleanup.TryParseArguments(
            ["--finalize-uninstall", "1", "2", "3", "4"], out _));
        Assert.False(SetupPostExitCleanup.TryParseArguments(
            [.. args, "other"], out _));
        Assert.False(SetupPostExitCleanup.TryParseArguments(
            ["--finalize-uninstall", "-1", "2", "3", "4", "5"], out _));
        Assert.False(SetupPostExitCleanup.TryParseArguments(
            ["--finalize-uninstall", "1", "0", "3", "4", "5"], out _));
        Assert.False(SetupPostExitCleanup.TryParseArguments(
            ["--finalize-uninstall", "1", "2", "0", "0", "0"], out _));
        Assert.False(SetupPostExitCleanup.TryParseArguments(
            ["--uninstall", "1", "2", "3", "4", "5"], out _));
        Assert.False(SetupPostExitCleanup.TryParseArguments(
            ["--finalize-uninstall", "1", "2", "3", "4", "5; rm"], out _));
    }

    [Fact]
    public void ParentPidMustAlsoMatchProcessStartTime()
    {
        using var current = Process.GetCurrentProcess();
        var request = SetupPostExitCleanup.ForCurrentProcess(
            new InstalledRootIdentity(1, 2, 3));
        Assert.Equal(current.Id, request.ParentProcessId);
        Assert.True(SetupPostExitCleanup.IsOriginalParentRunning(request));
        Assert.False(SetupPostExitCleanup.IsOriginalParentRunning(
            request with { ParentStartUtcTicks = request.ParentStartUtcTicks + 1 }));
        Assert.False(SetupPostExitCleanup.IsOriginalParentRunning(
            request with { ParentProcessId = int.MaxValue }));
    }

    [Fact]
    public void WindowsWorkerLocationMustBePrivateTempStaging()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        var other = Directory.CreateTempSubdirectory(
            "OmegaDrive-OtherFinalizer-").FullName;
        try
        {
            var copied = Path.Combine(staging,
                SetupPostExitCleanup.FinalizerExecutableName);
            File.WriteAllText(copied, "synthetic helper");
            Assert.Equal(staging,
                SetupPostExitCleanup.VerifyPrivateWorkerLocation(copied));

            Assert.Throws<InvalidDataException>(() =>
                SetupPostExitCleanup.VerifyPrivateWorkerLocation(
                    Path.Combine(other,
                        SetupPostExitCleanup.FinalizerExecutableName)));
            Assert.Throws<InvalidDataException>(() =>
                SetupPostExitCleanup.VerifyPrivateWorkerLocation(
                    Path.Combine(staging, "Untrusted.exe")));
            Assert.Throws<InvalidDataException>(() =>
                SetupPostExitCleanup.VerifyPrivateWorkerLocation(
                    Path.Combine(staging, "subdir",
                        SetupPostExitCleanup.FinalizerExecutableName)));
        }
        finally
        {
            SetupStagingDirectory.DeleteIfExists(staging);
            Directory.Delete(other);
        }
    }

    [Fact]
    public void WindowsAuditMarkersAreCreateNewAndCannotBeOverwritten()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        try
        {
            var path = SetupPostExitCleanup.WriteStatus(staging,
                "pending", "waiting for the original setup");
            Assert.Contains("waiting", File.ReadAllText(path));
            Assert.Throws<IOException>(() =>
                SetupPostExitCleanup.WriteStatus(staging,
                    "pending", "injected replacement"));
            Assert.Contains("waiting", File.ReadAllText(path));
            var success = SetupPostExitCleanup.WriteStatus(
                staging, "success", "completed");
            Assert.Contains("completed", File.ReadAllText(success));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SetupPostExitCleanup.WriteStatus(staging,
                    "untrusted", "not a valid marker"));
            Assert.Empty(Directory.GetFiles(staging,
                ".OmegaDrive-Anchor-*.lock"));
        }
        finally
        {
            SetupStagingDirectory.DeleteIfExists(staging);
        }
    }

    [Fact]
    public void WindowsFinalizerRemovesOnlyTheOriginalRootAfterParentExit()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var scratch = Directory.CreateTempSubdirectory(
            "OmegaDrive-FinalizerTest-").FullName;
        var staging = SetupStagingDirectory.Create();
        var programFiles = Path.Combine(scratch, "ProgramFiles");
        var install = Path.Combine(programFiles, "TheEasyWayForDrivers");
        var updater = Path.Combine(install, "Updater");
        try
        {
            Directory.CreateDirectory(updater);
            var canary = Path.Combine(updater, "legacy.exe");
            File.WriteAllText(canary, "old version");
            var identity = SetupInstalledRootCleanup.CaptureWindowsIdentity(
                install, programFiles);
            var request = SetupPostExitCleanup.ForCurrentProcess(identity);
            // Alter start ticks to simulate parent already exited; a reused
            // PID must never cause the finalizer to wait for our test runner.
            request = request with {
                ParentStartUtcTicks = request.ParentStartUtcTicks + 1
            };
            var worker = Path.Combine(staging,
                SetupPostExitCleanup.FinalizerExecutableName);

            Assert.Equal(InstallRootCleanupOutcome.Removed,
                SetupPostExitCleanup.Finish(
                    request, worker, install, programFiles));
            Assert.False(Directory.Exists(install));
            Assert.True(File.Exists(Path.Combine(
                staging, "cleanup-success.txt")));
            Assert.False(File.Exists(Path.Combine(
                staging, "cleanup-failure.txt")));
        }
        finally
        {
            SetupStagingDirectory.DeleteIfExists(staging);
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void WindowsFinalizerRefusesReinstalledRootWithRecordedIdentity()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var scratch = Directory.CreateTempSubdirectory(
            "OmegaDrive-FinalizerReinstall-").FullName;
        var staging = SetupStagingDirectory.Create();
        var programFiles = Path.Combine(scratch, "ProgramFiles");
        var install = Path.Combine(programFiles, "TheEasyWayForDrivers");
        var moved = Path.Combine(scratch, "previous");
        try
        {
            Directory.CreateDirectory(install);
            var identity = SetupInstalledRootCleanup.CaptureWindowsIdentity(
                install, programFiles);
            Directory.Move(install, moved);
            Directory.CreateDirectory(install);
            var canary = Path.Combine(install, "new.exe");
            File.WriteAllText(canary, "new installation");
            var request = SetupPostExitCleanup.ForCurrentProcess(identity);
            request = request with {
                ParentStartUtcTicks = request.ParentStartUtcTicks + 1
            };
            Assert.Throws<InvalidDataException>(() =>
                SetupPostExitCleanup.Finish(request,
                    Path.Combine(staging,
                        SetupPostExitCleanup.FinalizerExecutableName),
                    install, programFiles));
            Assert.Equal("new installation", File.ReadAllText(canary));
            Assert.True(Directory.Exists(moved));
        }
        finally
        {
            SetupStagingDirectory.DeleteIfExists(staging);
            Directory.Delete(scratch, recursive: true);
        }
    }
}
