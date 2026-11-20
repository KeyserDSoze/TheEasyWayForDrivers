namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Uninstall cleanup for the exact Program Files/OmegaDrive installation root
/// (#596). Never schedule a shell rmdir for later path resolution. If the
/// setup process is hosted inside the target root, leave it untouched and
/// report pending; a detached setup can delete it synchronously with the
/// guarded native tree walker. Automatic post-exit cleanup remains #595.
/// </summary>
public enum InstallRootCleanupOutcome
{
    Removed,
    AlreadyAbsent,
    PendingRunningSetup
}

public static class SetupInstalledRootCleanup
{
    private const string InstallDirectory = "TheEasyWayForDrivers";

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
        Action? beforeDeleteForTests)
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

        // No volume-root, UNC or device path may act as Program Files.
        if (string.Equals(programFiles, Path.GetPathRoot(programFiles),
                comparison) ||
            (OperatingSystem.IsWindows() &&
                (root.StartsWith(@"\\", StringComparison.Ordinal) ||
                 programFiles.StartsWith(@"\\", StringComparison.Ordinal))) ||
            !string.Equals(root, Path.Combine(programFiles, InstallDirectory),
                comparison))
            throw new InvalidDataException(
                "La cancellazione è consentita solo per la root installata OmegaDrive.");

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
            return InstallRootCleanupOutcome.AlreadyAbsent;
        }
        catch (DirectoryNotFoundException)
        {
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

        beforeDeleteForTests?.Invoke();
        SetupTreeCopyGuard.DeleteDirectoryIfExists(root);
        if (Directory.Exists(root))
            throw new IOException(
                "La root installata non è stata rimossa completamente.");
        return InstallRootCleanupOutcome.Removed;
    }
}
