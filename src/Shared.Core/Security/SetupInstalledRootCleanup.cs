namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Uninstall cleanup for the exact Program Files/OmegaDrive installation root
/// (#596). Never schedule a shell rmdir for later path resolution. If the
/// setup process is hosted inside the target root, leave it untouched and
/// report pending; a detached setup can delete it synchronously with the
/// guarded native tree walker. #597 pins the Windows file identity across
/// preflight and deletion. Automatic post-exit cleanup remains #595.
/// </summary>
public enum InstallRootCleanupOutcome
{
    Removed,
    AlreadyAbsent,
    PendingRunningSetup
}

/// <summary>
/// Windows volume and file index of an anchored installation root.
/// A post-exit helper must use the ID observed BEFORE the initiating setup
/// exits, never trust that the same path still denotes that installation.
/// </summary>
public readonly record struct InstalledRootIdentity(
    uint VolumeSerialNumber, uint FileIndexHigh, uint FileIndexLow);

public static class SetupInstalledRootCleanup
{
    private const string InstallDirectory = "TheEasyWayForDrivers";

    public static InstalledRootIdentity CaptureWindowsIdentity(
        string installRoot, string programFilesRoot)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        var root = ValidateScope(installRoot, programFilesRoot);
        using var anchor = SetupDirectoryAnchor.Open(root);
        var native = WindowsHandleRelativeFile.ReadDirectoryIdentity(
            anchor.LeafDirectoryHandle);
        return new InstalledRootIdentity(native.VolumeSerialNumber,
            native.FileIndexHigh, native.FileIndexLow);
    }

    // #597: require the ID captured by the initiating process, not an ID
    // freshly taken from whatever now occupies that path after process exit.
    public static InstallRootCleanupOutcome CleanupIfMatchingIdentity(
        string installRoot, string programFilesRoot, string? runningSetupPath,
        InstalledRootIdentity original)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        return CleanupCore(installRoot, programFilesRoot, runningSetupPath,
            beforeDeleteForTests: null, original);
    }

    public static InstallRootCleanupOutcome Cleanup(
        string installRoot, string programFilesRoot, string? runningSetupPath) =>
        CleanupCore(installRoot, programFilesRoot, runningSetupPath,
            beforeDeleteForTests: null);

    // Deterministic test seam between validating the target and invoking
    // the native guarded deletion. Production cannot inject this callback.
    internal static InstallRootCleanupOutcome CleanupWithBeforeDeleteForTests(
        string installRoot, string programFilesRoot, string? runningSetupPath,
        Action beforeDelete)
    {
        ArgumentNullException.ThrowIfNull(beforeDelete);
        return CleanupCore(installRoot, programFilesRoot, runningSetupPath,
            beforeDelete);
    }

    private static InstallRootCleanupOutcome CleanupCore(
        string installRoot, string programFilesRoot, string? runningSetupPath,
        Action? beforeDeleteForTests,
        InstalledRootIdentity? originalIdentity = null)
    {
        var root = ValidateScope(installRoot, programFilesRoot);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        // Fail closed for pre-existing redirects, including an ancestor.
        // Additional verification is performed by the native walker at
        // each deletion; this preflight does not claim to be atomic.
        SetupStagingDirectory.EnsureNoReparseAncestors(root);
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(root);
        }
        catch (FileNotFoundException)
        {
            if (originalIdentity is not null)
                throw new InvalidDataException(
                    "La root originaria non è più presente.");
            return InstallRootCleanupOutcome.AlreadyAbsent;
        }
        catch (DirectoryNotFoundException)
        {
            if (originalIdentity is not null)
                throw new InvalidDataException(
                    "La root originaria non è più presente.");
            return InstallRootCleanupOutcome.AlreadyAbsent;
        }

        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                "La root installata non è una directory regolare.");

        // If the identity of the running executable is unavailable, do
        // not assume a detached setup: fail safe with visible pending state.
        if (string.IsNullOrWhiteSpace(runningSetupPath) ||
            !Path.IsPathFullyQualified(runningSetupPath))
            return InstallRootCleanupOutcome.PendingRunningSetup;

        var executable = Path.GetFullPath(runningSetupPath);
        if (string.Equals(executable, root, comparison) ||
            executable.StartsWith(root + Path.DirectorySeparatorChar,
                comparison))
            return InstallRootCleanupOutcome.PendingRunningSetup;

        if (OperatingSystem.IsWindows())
        {
            // Capture the first directory's identity while anchored and
            // lock-fenced; compare a recorded cross-process identity BEFORE
            // any writes. The walker checks the SAME ID again on its own
            // pinned root handle, after test-injected/concurrent swaps.
            var observed = CaptureWindowsIdentity(root, programFilesRoot);
            if (originalIdentity is { } wanted && wanted != observed)
                throw new InvalidDataException(
                    "Installazione differente da quella selezionata per la rimozione.");
            beforeDeleteForTests?.Invoke();
            SetupTreeCopyGuard.DeleteDirectoryIfExistsWithExpectedIdentity(
                root, new WindowsHandleRelativeFile.DirectoryIdentity(
                    observed.VolumeSerialNumber, observed.FileIndexHigh,
                    observed.FileIndexLow));
        }
        else
        {
            if (originalIdentity is not null)
                throw new PlatformNotSupportedException();
            beforeDeleteForTests?.Invoke();
            SetupTreeCopyGuard.DeleteDirectoryIfExists(root);
        }

        if (Directory.Exists(root))
            throw new IOException(
                "La root installata non è stata rimossa completamente.");
        return InstallRootCleanupOutcome.Removed;
    }

    private static string ValidateScope(
        string installRoot, string programFilesRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(programFilesRoot);
        if (!Path.IsPathFullyQualified(installRoot) ||
            !Path.IsPathFullyQualified(programFilesRoot))
            throw new InvalidDataException(
                "La cancellazione richiede percorsi assoluti attendibili.");

        var root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(installRoot));
        var programFiles = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(programFilesRoot));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(programFiles, Path.GetPathRoot(programFiles),
                comparison) ||
            (OperatingSystem.IsWindows() &&
                (root.StartsWith(@"\\", StringComparison.Ordinal) ||
                 programFiles.StartsWith(@"\\", StringComparison.Ordinal))) ||
            !string.Equals(root, Path.Combine(programFiles, InstallDirectory),
                comparison))
            throw new InvalidDataException(
                "La cancellazione è consentita solo per la root installata OmegaDrive.");

        return root;
    }
}
