using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// A fail-closed preflight for the installer and rollback's legacy
/// path-based tree copy. It rejects pre-existing file or directory reparse
/// points and hardlinked files throughout source and destination BEFORE
/// destructive work (#576/#577). File copies on Windows publish a new
/// temporary inode by rename rather than overwriting an existing inode
/// (#578). Parent-path swaps remain a separate risk (#529).
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
        var temp = Path.Combine(parent,
            ".OmegaDrive-Copy-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, 81920))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            beforePublishForTests?.Invoke();

            // Parent names still need a path-based lookup (tracked by
            // #529). Recheck to refuse a visible parent redirect before
            // publishing, but do not truncate an existing target inode.
            SetupStagingDirectory.EnsureNoReparseAncestors(to);
            var flags = MoveFileWriteThrough |
                (overwrite ? MoveFileReplaceExisting : 0u);
            if (!MoveFileExW(temp, to, flags))
                throw new IOException(
                    "Impossibile pubblicare il file verificato della copia.");
        }
        finally
        {
            // No temporary file must survive an error, cancellation or
            // completed rename. A cleanup failure propagates to the caller.
            if (File.Exists(temp))
                File.Delete(temp);
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
    private const uint MoveFileReplaceExisting = 0x00000001;
    private const uint MoveFileWriteThrough = 0x00000008;

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(
        string existingFileName, string newFileName, uint flags);

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
