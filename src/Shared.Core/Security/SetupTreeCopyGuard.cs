namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// A fail-closed preflight for the installer and rollback's legacy
/// path-based tree copy. It rejects pre-existing file or directory reparse
/// points throughout source and destination BEFORE destructive work (#576).
/// It is NOT atomic against same-privilege concurrent namespace swaps (#529).
/// </summary>
public static class SetupTreeCopyGuard
{
    public static bool SourceDirectoryExists(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var path = Path.GetFullPath(source);
        SetupStagingDirectory.EnsureNoReparseAncestors(path);
        if (File.Exists(path))
            throw new InvalidDataException(
                "La sorgente di copia non è una directory.");

        return Directory.Exists(path);
    }

    public static void ReplaceDirectory(string source, string destination)
    {
        var (from, to) = GetDistinctPaths(source, destination);

        // Check the FULL trees before deleting even one old file.
        ValidateCopy(from, to);
        if (Directory.Exists(to))
            Directory.Delete(to, recursive: true); // must not swallow errors

        CopyDirectoryChecked(from, to);
    }

    public static void CopyDirectoryIfExists(string source, string destination)
    {
        var (from, to) = GetDistinctPaths(source, destination);
        if (!SourceDirectoryExists(from))
            return;

        ValidateCopy(from, to);
        CopyDirectoryChecked(from, to);
    }

    public static void CopySingleFile(
        string source, string destination, bool overwrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var from = Path.GetFullPath(source);
        var to = Path.GetFullPath(destination);
        SetupStagingDirectory.EnsureNoReparseAncestors(from);
        SetupStagingDirectory.EnsureNoReparseAncestors(to);
        ValidateRegularFile(from);
        if (File.Exists(to))
            ValidateRegularFile(to);
        File.Copy(from, to, overwrite);
    }

    private static (string Source, string Destination) GetDistinctPaths(
        string source, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        var to = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        // Reject self-deletion and recursion into a parent/child of itself.
        if (string.Equals(from, to, comparison) ||
            to.StartsWith(from + Path.DirectorySeparatorChar, comparison) ||
            from.StartsWith(to + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException(
                "Sorgente e destinazione della copia si sovrappongono.");

        return (from, to);
    }

    private static void ValidateCopy(string from, string to)
    {
        SetupStagingDirectory.EnsureNoReparseAncestors(from);
        SetupStagingDirectory.EnsureNoReparseAncestors(to);
        ValidateTree(from);
        if (Directory.Exists(to))
            ValidateTree(to);
        else if (File.Exists(to))
            throw new InvalidDataException(
                "La destinazione della copia non è una directory.");
    }

    private static void ValidateTree(string directory)
    {
        ValidateDirectory(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            // File.GetAttributes returns ReparsePoint even for directory
            // links; NEVER enumerate a child until its attributes pass.
            var attributes = File.GetAttributes(entry);
            RejectReparse(attributes);
            if ((attributes & FileAttributes.Directory) != 0)
                ValidateTree(entry);
            else if ((attributes & FileAttributes.Device) != 0)
                throw new InvalidDataException(
                    "La copia contiene un file speciale non consentito.");
        }
    }

    private static void CopyDirectoryChecked(string from, string to)
    {
        // All path checks are repeated while copying. They cannot defeat
        // a concurrent same-privilege rename, which remains #529.
        SetupStagingDirectory.EnsureNoReparseAncestors(from);
        SetupStagingDirectory.EnsureNoReparseAncestors(to);
        ValidateDirectory(from);
        Directory.CreateDirectory(to);
        ValidateDirectory(to);

        foreach (var entry in Directory.EnumerateFileSystemEntries(from))
        {
            var attributes = File.GetAttributes(entry);
            RejectReparse(attributes);
            var childDestination = Path.Combine(to, Path.GetFileName(entry));
            SetupStagingDirectory.EnsureNoReparseAncestors(childDestination);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                CopyDirectoryChecked(entry, childDestination);
                continue;
            }

            ValidateRegularFile(entry);
            if (File.Exists(childDestination))
                ValidateRegularFile(childDestination);
            File.Copy(entry, childDestination, overwrite: true);
        }
    }

    private static void ValidateRegularFile(string path)
    {
        var attributes = File.GetAttributes(path);
        RejectReparse(attributes);
        if ((attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw new InvalidDataException(
                "La copia contiene un file non regolare.");
    }

    private static void ValidateDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        RejectReparse(attributes);
        if ((attributes & FileAttributes.Directory) == 0)
            throw new InvalidDataException(
                "La copia contiene una directory non valida.");
    }

    private static void RejectReparse(FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                "La copia contiene un collegamento non consentito.");
    }
}
