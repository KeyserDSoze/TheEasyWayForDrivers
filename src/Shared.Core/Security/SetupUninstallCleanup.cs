namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Uninstall-only deletion of OmegaDrive's installed components and program
/// data (#594). Do not broaden the Temp-only SetupStagingDirectory cleanup:
/// these are distinct filesystem trust boundaries. The native tree guard
/// refuses reparse points and hardlinked files during Windows deletion.
/// The installed Updater is left for the separate post-exit cleanup path.
/// </summary>
public static class SetupUninstallCleanup
{
    private const string ApplicationDirectory = "TheEasyWayForDrivers";

    public static void DeleteApplicationTrees(
        string installRoot, string programDataRoot)
    {
        var install = ValidateApplicationRoot(installRoot);
        var data = ValidateApplicationRoot(programDataRoot);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(install, data, comparison) ||
            IsWithin(install, data, comparison) ||
            IsWithin(data, install, comparison))
            throw new InvalidDataException(
                "Le radici di installazione e dati devono essere distinte.");

        // The two leaf names are fixed, not derived from external input.
        // Failure to delete either tree is visible to SetupEngine: never
        // swallow invalid links and then display "uninstall complete".
        SetupTreeCopyGuard.DeleteDirectoryIfExists(
            Path.Combine(install, "Service"));
        SetupTreeCopyGuard.DeleteDirectoryIfExists(
            Path.Combine(install, "Desktop"));
        SetupTreeCopyGuard.DeleteDirectoryIfExists(data);
    }

    private static string ValidateApplicationRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!string.Equals(Path.GetFileName(path), ApplicationDirectory,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal) ||
            string.IsNullOrEmpty(Path.GetDirectoryName(path)))
            throw new InvalidDataException(
                "La disinstallazione richiede una root OmegaDrive esplicita.");
        return path;
    }

    private static bool IsWithin(
        string candidate, string parent, StringComparison comparison) =>
        candidate.StartsWith(
            parent + Path.DirectorySeparatorChar, comparison);
}
