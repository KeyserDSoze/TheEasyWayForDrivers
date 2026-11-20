using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// A fail-closed preflight for the installer and rollback's legacy
/// path-based tree copy. It rejects pre-existing file or directory reparse
/// points and hardlinked files throughout source and destination BEFORE
/// destructive work (#576/#577). File copies on Windows publish a new
/// temporary inode by rename rather than overwriting an existing inode
/// (#578). Destination parents are pinned during the copy (#579), and
/// copy publication now uses file+directory handles (#580). Windows
/// destination deletion rejects reparse points at each child and never
/// invokes recursive framework deletion (#581). Empty directory removal
/// also verifies object identity and uses its parent's native handle
/// (#582), and source files are opened relative to pinned directories
/// (#583). Windows source entry enumerations are checked while holding
/// source directory anchors before target creation (#584); remaining
/// path-based tree operations are still tracked in #529.
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
        ReplaceDirectoryCore(from, to, beforeDeletionForTests: null,
            beforeEmptyDeleteForTests: null);
    }


    // A synchronized test-only seam after the complete preflight but
    // before the first destination deletion (#581). Production callers
    // cannot inject callbacks through the public ReplaceDirectory API.
    internal static void ReplaceDirectoryWithBeforeDeletionForTests(
        string source, string destination, Action beforeDeletion)
    {
        ArgumentNullException.ThrowIfNull(beforeDeletion);
        var (from, to) = GetDistinctPaths(source, destination);
        ReplaceDirectoryCore(from, to, beforeDeletion,
            beforeEmptyDeleteForTests: null);
    }

    // Internal regression hook positioned AFTER leaf anchor disposal,
    // BEFORE native parent-relative empty-directory deletion (#582).
    internal static void ReplaceDirectoryWithBeforeEmptyDeleteForTests(
        string source, string destination, Action<string> beforeEmptyDelete)
    {
        ArgumentNullException.ThrowIfNull(beforeEmptyDelete);
        var (from, to) = GetDistinctPaths(source, destination);
        ReplaceDirectoryCore(from, to, beforeDeletionForTests: null,
            beforeEmptyDeleteForTests: beforeEmptyDelete);
    }

    private static void ReplaceDirectoryCore(
        string from, string to, Action? beforeDeletionForTests,
        Action<string>? beforeEmptyDeleteForTests)
    {
        ValidateCopy(from, to);
        beforeDeletionForTests?.Invoke();

        if (Directory.Exists(to))
        {
            if (OperatingSystem.IsWindows())
                DeleteExistingTreeWindows(to, beforeEmptyDeleteForTests);
            else
                Directory.Delete(to, recursive: true);
        }

        CopyDirectoryChecked(from, to);
    }

    private static void DeleteExistingTreeWindows(
        string directory, Action<string>? beforeEmptyDeleteForTests)
    {
        // Hold the parent anchor throughout leaf traversal and deletion.
        // The old leaf's exact identity is recorded before releasing its
        // rename fence. The final delete opens the child RELATIVE to the
        // pinned parent and refuses a replacement of the name (#582).
        SetupStagingDirectory.EnsureNoReparseAncestors(directory);
        var parentPath = Path.GetDirectoryName(directory) ??
            throw new InvalidDataException(
                "La directory da rimuovere non ha un parent valido.");
        using var parentAnchor = SetupDirectoryAnchor.Open(parentPath);
        WindowsHandleRelativeFile.DirectoryIdentity expected;
        using (var anchor = SetupDirectoryAnchor.Open(directory))
        {
            expected = WindowsHandleRelativeFile.ReadDirectoryIdentity(
                anchor.LeafDirectoryHandle);
            // Take a stable list and revalidate EVERY entry, including
            // deeper subtrees, before deleting the first file. Exclude
            // precisely this anchor's exclusive internal lock. A junction
            // injected after the initial ReplaceDirectory preflight must
            // fail before removing any previous installation content.
            var entries = Directory.GetFileSystemEntries(directory)
                .Where(entry => !string.Equals(
                    Path.GetFileName(entry), anchor.RenameFenceName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var entry in entries)
            {
                var attributes = File.GetAttributes(entry);
                RejectReparse(attributes);
                if ((attributes & FileAttributes.Directory) != 0)
                    ValidateTree(entry);
                else
                    ValidateRegularFile(entry);
            }

            foreach (var entry in entries)
            {
                var attributes = File.GetAttributes(entry);
                RejectReparse(attributes);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    DeleteExistingTreeWindows(entry, beforeEmptyDeleteForTests);
                }
                else
                {
                    // Delete a verified single-link regular file using
                    // its native handle relative to the pinned parent.
                    WindowsHandleRelativeFile.DeleteExistingRegularFile(
                        anchor.LeafDirectoryHandle, Path.GetFileName(entry));
                }
            }
        }

        // Leaf lock has been released, but the parent anchor remains
        // pinned. Inject a deterministic swap at precisely this window
        // in Windows regression tests; production has no callback.
        beforeEmptyDeleteForTests?.Invoke(directory);
        WindowsHandleRelativeFile.DeleteVerifiedEmptyDirectory(
            parentAnchor.LeafDirectoryHandle,
            Path.GetFileName(directory), expected);
    }

    /// <summary>
    /// Cleanup for a previously-created staging tree. On Windows, reuse
    /// the same pinned-parent/native-handle deletion walker used for
    /// replacing installed files, never a path-based recursive delete (#585).
    /// Validate the full tree before deleting the first entry.
    /// </summary>
    public static void DeleteDirectoryIfExists(string directory) =>
        DeleteDirectoryIfExistsCore(directory, beforeDeletionForTests: null);

    // Test-only seam after the complete preflight, before any deletion.
    internal static void DeleteDirectoryIfExistsWithBeforeDeletionForTests(
        string directory, Action beforeDeletion)
    {
        ArgumentNullException.ThrowIfNull(beforeDeletion);
        DeleteDirectoryIfExistsCore(directory, beforeDeletion);
    }

    private static void DeleteDirectoryIfExistsCore(
        string directory, Action? beforeDeletionForTests)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        SetupStagingDirectory.EnsureNoReparseAncestors(path);
        if (!Directory.Exists(path))
        {
            if (File.Exists(path))
                throw new InvalidDataException(
                    "La directory temporanea è stata sostituita da un file.");
            return;
        }

        ValidateTree(path);
        beforeDeletionForTests?.Invoke();
        if (OperatingSystem.IsWindows())
            DeleteExistingTreeWindows(path, beforeEmptyDeleteForTests: null);
        else
            Directory.Delete(path, recursive: true);
    }

    public static void CopyDirectoryIfExists(string source, string destination)
    {
        var (from, to) = GetDistinctPaths(source, destination);
        if (!SourceDirectoryExists(from))
            return;

        ValidateCopy(from, to);
        CopyDirectoryChecked(from, to);
    }

    /// <summary>
    /// Publish the installer executable to the installed Updater directory.
    /// On Windows the existing install root is pinned before opening or
    /// creating the Updater child relative to the verified parent handle.
    /// Legacy aliases are removed only by a verified native file handle
    /// under that same pinned directory (#587).
    /// </summary>
    public static void CopySetupUpdater(string source, string installRoot) =>
        CopySetupUpdaterCore(source, installRoot, beforeCopyForTests: null);

    // Internal deterministic synchronization seam after acquiring both
    // directory anchors, before any legacy deletion or installer copy.
    internal static void CopySetupUpdaterWithBeforeCopyForTests(
        string source, string installRoot, Action<string> beforeCopy)
    {
        ArgumentNullException.ThrowIfNull(beforeCopy);
        CopySetupUpdaterCore(source, installRoot, beforeCopy);
    }

    private static void CopySetupUpdaterCore(
        string source, string installRoot, Action<string>? beforeCopyForTests)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        var from = Path.GetFullPath(source);
        var root = Path.GetFullPath(installRoot);
        var updater = Path.Combine(root, "Updater");
        var destination = Path.Combine(updater, "OmegaDrive-Setup.exe");
        var legacy = Path.Combine(updater,
            "TheEasyWayForDrivers-Setup.exe");
        var sameSource = string.Equals(from, destination,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

        SetupStagingDirectory.EnsureNoReparseAncestors(root);
        if (OperatingSystem.IsWindows())
        {
            // Service/Desktop installation creates the install root before
            // this step. Missing or redirected roots must fail closed.
            using var rootAnchor = SetupDirectoryAnchor.Open(root);
            using var updaterAnchor = SetupDirectoryAnchor.OpenOrCreateChild(
                rootAnchor.LeafDirectoryHandle, "Updater");
            beforeCopyForTests?.Invoke(updater);

            // The native opener refuses symlinks, junctions and hardlinks
            // before deleting the alias. A suspicious alias is fatal; do
            // not silently continue as though a legitimate alias vanished.
            // #588: File.Exists(legacy) would silently return false for a
            // dangling symlink (or an unreadable child), leaving an unsafe
            // alias. Only a native STATUS_OBJECT_NAME_NOT_FOUND is absence.
            WindowsHandleRelativeFile.TryDeleteExistingRegularFile(
                updaterAnchor.LeafDirectoryHandle,
                "TheEasyWayForDrivers-Setup.exe");

            if (!sameSource)
                CopySingleFile(from, destination, overwrite: true);
            return;
        }

        // Non-Windows test hosts retain their portable setup semantics.
        SetupStagingDirectory.EnsureNoReparseAncestors(updater);
        Directory.CreateDirectory(updater);
        SetupStagingDirectory.EnsureNoReparseAncestors(updater);
        if (File.Exists(legacy))
        {
            try { File.Delete(legacy); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        beforeCopyForTests?.Invoke(updater);
        if (!sameSource)
            CopySingleFile(from, destination, overwrite: true);
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
        CopyFileWithoutTargetWrites(from, to, overwrite,
            beforePublishForTests: null);
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
            else
                ValidateRegularFile(entry);
        }
    }

    // Test-only synchronization seam: invoked after an entry is enumerated
    // but immediately before its native, handle-relative validation (#584).
    internal static void CopyDirectoryIfExistsWithBeforeEntryValidationForTests(
        string source, string destination, Action<string> beforeEntryValidation)
    {
        ArgumentNullException.ThrowIfNull(beforeEntryValidation);
        var (from, to) = GetDistinctPaths(source, destination);
        if (!SourceDirectoryExists(from))
            return;

        ValidateCopy(from, to);
        CopyDirectoryChecked(from, to, beforeEntryValidation);
    }

    // Windows-only synchronization seam after the destination parent is
    // pinned and before its child is opened/created relative to the handle.
    // Production callers cannot supply this callback (#593).
    internal static void CopyDirectoryIfExistsWithBeforeDestinationOpenForTests(
        string source, string destination, Action<string> beforeDestinationOpen)
    {
        ArgumentNullException.ThrowIfNull(beforeDestinationOpen);
        var (from, to) = GetDistinctPaths(source, destination);
        if (!SourceDirectoryExists(from))
            return;
        ValidateCopy(from, to);
        CopyDirectoryChecked(from, to,
            beforeEntryValidationForTests: null,
            beforeDestinationOpenForTests: beforeDestinationOpen);
    }

    private static void CopyDirectoryChecked(
        string from, string to,
        Action<string>? beforeEntryValidationForTests = null,
        Action<string>? beforeDestinationOpenForTests = null)
    {
        // Verify the source AND destination paths before any work.
        // Windows holds a native anchor throughout enumerating and
        // recursively copying each source directory. The callback is
        // strictly internal and is never provided by production (#584).
        SetupStagingDirectory.EnsureNoReparseAncestors(from);
        SetupStagingDirectory.EnsureNoReparseAncestors(to);
        ValidateDirectory(from);

        if (OperatingSystem.IsWindows())
        {
            using var sourceAnchor = SetupDirectoryAnchor.Open(from);
            // Do not include this anchor's internal delete-on-close fence
            // in the source payload. Exclude ONLY the exact random name.
            var entries = Directory.GetFileSystemEntries(from)
                .Where(entry => !string.Equals(
                    Path.GetFileName(entry), sourceAnchor.RenameFenceName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // Validate EVERY immediate child relative to the verified
            // parent handle before even creating the target directory.
            // A new symlink/hardlink after the path-based preflight
            // cannot be mistaken for a regular child during enumeration.
            foreach (var entry in entries)
            {
                beforeEntryValidationForTests?.Invoke(entry);
                var attributes = File.GetAttributes(entry);
                RejectReparse(attributes);
                var component = Path.GetFileName(entry);

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    using var child = WindowsHandleRelativeFile
                        .OpenExistingDirectory(
                            sourceAnchor.LeafDirectoryHandle, component,
                            isLeaf: false);
                    _ = WindowsHandleRelativeFile.ReadDirectoryIdentity(child);
                }
                else
                {
                    using var input = WindowsHandleRelativeFile
                        .OpenVerifiedSourceForCopy(
                            sourceAnchor.LeafDirectoryHandle, component);
                }
            }

            // Pin the existing destination parent BEFORE opening or
            // creating the leaf. Resolve precisely one component through
            // NtCreateFile relative to that verified handle; never
            // re-resolve the absolute destination with CreateDirectoryW.
            // Both rename fences stay alive through the entire copy (#593).
            var destinationLeaf = Path.TrimEndingDirectorySeparator(to);
            var destinationParent = Path.GetDirectoryName(destinationLeaf)
                ?? throw new InvalidDataException(
                    "La destinazione non ha un parent valido.");
            using var parentAnchor = SetupDirectoryAnchor.Open(destinationParent);
            beforeDestinationOpenForTests?.Invoke(destinationLeaf);
            using var destinationAnchor = SetupDirectoryAnchor.OpenOrCreateChild(
                parentAnchor.LeafDirectoryHandle,
                Path.GetFileName(destinationLeaf));

            CopyValidatedEntries(entries, to, beforeEntryValidationForTests);
            return;
        }

        // Preserve the prior path-based behavior for non-Windows hosts.
        Directory.CreateDirectory(to);
        ValidateDirectory(to);
        CopyValidatedEntries(Directory.GetFileSystemEntries(from), to,
            beforeEntryValidationForTests: null);
    }

    private static void CopyValidatedEntries(
        IEnumerable<string> entries, string to,
        Action<string>? beforeEntryValidationForTests)
    {
        foreach (var entry in entries)
        {
            var attributes = File.GetAttributes(entry);
            RejectReparse(attributes);
            var childDestination = Path.Combine(to, Path.GetFileName(entry));
            SetupStagingDirectory.EnsureNoReparseAncestors(childDestination);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                CopyDirectoryChecked(
                    entry, childDestination, beforeEntryValidationForTests);
                continue;
            }

            ValidateRegularFile(entry);
            if (File.Exists(childDestination))
                ValidateRegularFile(childDestination);
            CopyFileWithoutTargetWrites(entry, childDestination,
                overwrite: true, beforePublishForTests: null);
        }
    }


    /// <summary>
    /// Test-only hook at the precise boundary after creating a complete
    /// replacement file but before publishing its name. Production paths
    /// never provide a callback (#578).
    /// </summary>
    internal static void CopySingleFileWithBeforePublishForTests(
        string source, string destination, bool overwrite,
        Action beforePublish)
    {
        ArgumentNullException.ThrowIfNull(beforePublish);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var from = Path.GetFullPath(source);
        var to = Path.GetFullPath(destination);
        SetupStagingDirectory.EnsureNoReparseAncestors(from);
        SetupStagingDirectory.EnsureNoReparseAncestors(to);
        ValidateRegularFile(from);
        if (File.Exists(to))
            ValidateRegularFile(to);
        CopyFileWithoutTargetWrites(from, to, overwrite, beforePublish);
    }

    // Test-only hook after source ancestor pinning but before the child
    // is opened natively. Production calls keep both callbacks null.
    internal static void CopySingleFileWithBeforeSourceReadForTests(
        string source, string destination, bool overwrite,
        Action beforeSourceRead)
    {
        ArgumentNullException.ThrowIfNull(beforeSourceRead);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var from = Path.GetFullPath(source);
        var to = Path.GetFullPath(destination);
        SetupStagingDirectory.EnsureNoReparseAncestors(from);
        SetupStagingDirectory.EnsureNoReparseAncestors(to);
        ValidateRegularFile(from);
        if (File.Exists(to))
            ValidateRegularFile(to);
        CopyFileWithoutTargetWrites(from, to, overwrite,
            beforePublishForTests: null, beforeSourceReadForTests: beforeSourceRead);
    }

    private static void CopyFileWithoutTargetWrites(
        string from, string to, bool overwrite,
        Action? beforePublishForTests,
        Action? beforeSourceReadForTests = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Linux/macOS test hosts retain the old File.Copy semantics.
            File.Copy(from, to, overwrite);
            return;
        }

        // Anchor the SOURCE directory for the complete copy and publish,
        // as the target already is (#579). Only the leaf filename is
        // resolved natively relative to this verified parent handle.
        // A path swap after the first preflight cannot redirect reads
        // into a reparse point or a hardlinked external object (#583).
        var sourceParent = Path.GetDirectoryName(from) ??
            throw new InvalidDataException("Sorgente senza parent valido.");
        using var sourceAnchor = SetupDirectoryAnchor.Open(sourceParent);
        beforeSourceReadForTests?.Invoke();
        using var input = WindowsHandleRelativeFile.OpenVerifiedSourceForCopy(
            sourceAnchor.LeafDirectoryHandle, Path.GetFileName(from));

        // Never open the existing destination inode for write. Instead
        // create a new temporary file in the SAME directory/volume.
        // MOVEFILE_REPLACE_EXISTING replaces the directory ENTRY, so
        // an injected destination hardlink cannot overwrite its other name.
        var parent = Path.GetDirectoryName(to) ??
            throw new InvalidDataException("Destinazione file non valida.");
        SetupStagingDirectory.EnsureNoReparseAncestors(parent);

        // Keep the verified destination directory AND its rename fence
        // alive for the entire copy, rename and cleanup. The temporary
        // file is created relative to this pinned native handle, rather
        // than by resolving its full pathname again (#579).
        using var destinationAnchor = SetupDirectoryAnchor.Open(parent);
        var tempName =
            ".OmegaDrive-Copy-" + Guid.NewGuid().ToString("N") + ".tmp";
        using var output = WindowsHandleRelativeFile.CreateNewForCopy(
            destinationAnchor.LeafDirectoryHandle, tempName);
        try
        {
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
            beforePublishForTests?.Invoke();

            // Publish the original OPEN temporary file by handle and
            // resolve only the new single name relative to the pinned
            // destination directory. No MoveFileExW(tempPath, toPath)
            // and no window between closing the source and rename (#580).
            WindowsHandleRelativeFile.RenameCopyByHandle(
                output.SafeFileHandle,
                destinationAnchor.LeafDirectoryHandle,
                Path.GetFileName(to), overwrite);
        }
        catch
        {
            // Delete the SAME object represented by the output handle,
            // without looking up a possibly substituted temp pathname.
            WindowsHandleRelativeFile.DeleteCopyByHandleOnClose(
                output.SafeFileHandle);
            throw;
        }
    }

    private static void ValidateRegularFile(string path)
    {
        var attributes = File.GetAttributes(path);
        RejectReparse(attributes);
        if ((attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw new InvalidDataException(
                "La copia contiene un file non regolare.");

        // Hardlinks have no ReparsePoint bit: only a native handle can
        // reveal nNumberOfLinks. Check the actual opened file and fail
        // closed before copying or replacing any data (#577).
        if (OperatingSystem.IsWindows())
            VerifySingleLinkWindowsFile(path);
    }

    private static void ValidateDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        RejectReparse(attributes);
        if ((attributes & FileAttributes.Directory) == 0)
            throw new InvalidDataException(
                "La copia contiene una directory non valida.");
    }


    private static void VerifySingleLinkWindowsFile(string path)
    {
        // Open the LINK ITSELF (not its target) with minimum rights. Do
        // not read the contents, follow a reparse point or change metadata.
        // Share everything so this read-only inspection does not lock out
        // the installer's normal file replacement and rollback operations.
        using var handle = CreateFileW(path,
            FileReadAttributes, FileShareReadWriteDelete, IntPtr.Zero,
            OpenExisting, OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException(
                "Impossibile verificare il file della copia.");

        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException(
                "Impossibile verificare i collegamenti del file.");

        if ((info.FileAttributes & ReparsePointAttribute) != 0 ||
            (info.FileAttributes & DirectoryAttribute) != 0 ||
            info.NumberOfLinks != 1)
            throw new InvalidDataException(
                "La copia contiene un file condiviso o non regolare.");
    }

    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareReadWriteDelete = 0x00000007;
    private const uint OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint ReparsePointAttribute = 0x00000400;
    private const uint DirectoryAttribute = 0x00000010;

    // Layout of BY_HANDLE_FILE_INFORMATION: thirteen DWORDs, with no
    // padding between the low/high halves of each Windows FILETIME.
    [StructLayout(LayoutKind.Sequential)]
    private struct HandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file, out HandleFileInformation information);

    private static void RejectReparse(FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                "La copia contiene un collegamento non consentito.");
    }
}
