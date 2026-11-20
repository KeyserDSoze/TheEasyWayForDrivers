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
/// (#582); other TOCTOU remains #529.
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

    private static void CopyFileWithoutTargetWrites(
        string from, string to, bool overwrite,
        Action? beforePublishForTests)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Linux/macOS test hosts retain the old File.Copy semantics.
            File.Copy(from, to, overwrite);
            return;
        }

        // The FILE OPEN for the SOURCE is inspected on the same handle
        // that supplies the bytes. A name swap after preflight therefore
        // cannot redirect our reads to a reparse/hardlinked file (#578).
        using var input = OpenVerifiedWindowsSource(from);

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

    private static FileStream OpenVerifiedWindowsSource(string path)
    {
        // FILE_SHARE_READ | FILE_SHARE_DELETE permits images currently in
        // use but denies concurrent writes while their bytes are copied.
        var handle = CreateFileW(path,
            FileReadData | FileReadAttributes,
            FileShareReadDelete, IntPtr.Zero, OpenExisting,
            OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new IOException("Impossibile aprire la sorgente verificata.");
        }

        try
        {
            if (!GetFileInformationByHandle(handle, out var info))
                throw new IOException(
                    "Impossibile verificare la sorgente della copia.");
            if ((info.FileAttributes &
                    (ReparsePointAttribute | DirectoryAttribute)) != 0 ||
                info.NumberOfLinks != 1)
                throw new InvalidDataException(
                    "La sorgente della copia non è un file regolare isolato.");

            return new FileStream(handle, FileAccess.Read, 81920);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private const uint FileReadData = 0x00000001;
    private const uint FileShareReadDelete = 0x00000005;
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
