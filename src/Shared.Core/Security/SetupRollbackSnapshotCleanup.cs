namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Clears the previous OmegaDrive rollback snapshot before an update (#594).
/// Never use the Temp-only staging cleanup for ProgramData or swallow a
/// failed deletion: a partially stale snapshot must not be reused.
/// </summary>
public static class SetupRollbackSnapshotCleanup
{
    public static void DeletePreviousSnapshot(string programDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programDataRoot);
        var root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(programDataRoot));
        if (!string.Equals(Path.GetFileName(root),
                "TheEasyWayForDrivers",
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal) ||
            string.IsNullOrEmpty(Path.GetDirectoryName(root)))
            throw new InvalidDataException(
                "La root di rollback OmegaDrive non è valida.");

        // Validate and remove only the exact Rollback child. The Windows
        // native cleanup walker rejects reparse points and hardlinks.
        SetupTreeCopyGuard.DeleteDirectoryIfExists(
            Path.Combine(root, "Rollback"));
    }
}
