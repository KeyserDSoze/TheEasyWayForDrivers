using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Windows defense-in-depth for the installer staging directory (#562).
/// Pins every existing ancestor from the volume root down to the target
/// without FILE_SHARE_DELETE. An exclusive, delete-on-close leaf lock file
/// also prevents folder rename on Windows (which handle sharing alone did
/// not block in the initial #562 CI test). The lock is created relative to
/// the VERIFIED leaf directory handle, never via a fresh path lookup (#569).
/// Parent handles are opened before children and checked for reparse attributes.
/// Handle-relative child directories and files are supported (#568/#567);
/// root traversal remains path-based and full TOCTOU QA stays in #529.
/// </summary>
public sealed class SetupDirectoryAnchor : IDisposable
{
    private const uint FileReadAttributes = 0x80;
    private const uint FileTraverse = 0x20;
    private const uint FileAddFile = 0x02;
    private const uint FileAddSubdirectory = 0x04;
    private const uint FileShareReadWrite = 0x01 | 0x02;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint DirectoryAttribute = 0x10;
    private const uint ReparsePointAttribute = 0x400;

    private readonly List<SafeFileHandle> _handles = [];
    // Always created relative to the already-verified leaf directory handle.
    private SafeFileHandle? _renameFence;

    private SetupDirectoryAnchor() { }

    public static SetupDirectoryAnchor Open(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var fullPath = Path.GetFullPath(directory);

        if (!OperatingSystem.IsWindows())
        {
            SetupStagingDirectory.EnsureNoReparseAncestors(fullPath);
            if (!Directory.Exists(fullPath))
                throw new DirectoryNotFoundException(
                    "Directory di staging non disponibile.");
            return new SetupDirectoryAnchor();
        }

        // The elevated setup must stage on a local volume, never a UNC
        // share or a device namespace supplied via environment variables.
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException(
                "Lo staging richiede un disco locale Windows.");

        var anchor = new SetupDirectoryAnchor();
        try
        {
            var ancestors = new Stack<string>();
            for (DirectoryInfo? current = new(fullPath); current is not null;
                 current = current.Parent)
                ancestors.Push(current.FullName);

            // Once an ancestor is open without DELETE sharing, another
            // process cannot rename/remove that directory in the meantime.
            while (ancestors.TryPop(out var path))
            {
                // RootDirectory is used for native child and lock creation:
                // only the leaf needs traverse/create-child access. Parent
                // ancestors remain read-attributes-only.
                var access = FileReadAttributes |
                    (ancestors.Count == 0
                        ? FileTraverse | FileAddFile | FileAddSubdirectory
                        : 0u);
                var handle = CreateFileW(path, access,
                    FileShareReadWrite, IntPtr.Zero, OpenExisting,
                    BackupSemantics | OpenReparsePoint, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    throw new IOException(
                        $"Impossibile vincolare lo staging (Win32 {error}).");
                }

                anchor._handles.Add(handle);
                if (!GetFileInformationByHandle(handle, out var info))
                    throw new IOException(
                        "Impossibile verificare l'identità della directory di staging.");

                if ((info.FileAttributes & ReparsePointAttribute) != 0 ||
                    (info.FileAttributes & DirectoryAttribute) == 0)
                    throw new InvalidDataException(
                        "Lo staging contiene un collegamento o una directory non valida.");
            }

            // Directory handles alone did not veto renames in the #562
            // Windows regression. Create a delete-on-close child lock
            // relative to the exact verified LEAF handle. Do not re-open
            // fullPath: it might now designate a different directory.
            anchor._renameFence =
                WindowsHandleRelativeFile.CreateDeleteOnCloseLock(
                    anchor.LeafDirectoryHandle,
                    ".OmegaDrive-Anchor-" +
                        Guid.NewGuid().ToString("N") + ".lock");
            return anchor;
        }
        catch
        {
            anchor.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Open or create exactly one subdirectory relative to a pinned parent.
    /// Validate the returned handle before obtaining an exclusive lock
    /// through that same handle, without an absolute child path (#568).
    /// </summary>
    internal static SetupDirectoryAnchor OpenOrCreateChild(
        SafeFileHandle parent, string component)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();

        var anchor = new SetupDirectoryAnchor();
        try
        {
            var child = WindowsHandleRelativeFile.OpenOrCreateDirectory(
                parent, component);
            anchor._handles.Add(child);
            if (!GetFileInformationByHandle(child, out var info))
                throw new IOException(
                    "Impossibile verificare la directory temporanea.");

            if ((info.FileAttributes & ReparsePointAttribute) != 0 ||
                (info.FileAttributes & DirectoryAttribute) == 0)
                throw new InvalidDataException(
                    "Lo staging contiene un collegamento o una directory non valida.");

            anchor._renameFence =
                WindowsHandleRelativeFile.CreateDeleteOnCloseLock(
                    child, ".OmegaDrive-Anchor-" +
                        Guid.NewGuid().ToString("N") + ".lock");
            return anchor;
        }
        catch
        {
            anchor.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Stable native directory handle for creating a single relative child.
    /// The caller must keep this anchor alive while making the native call.
    /// </summary>
    internal SafeFileHandle LeafDirectoryHandle =>
        _handles.Count > 0 ? _handles[^1] :
            throw new PlatformNotSupportedException(
                "L'handle della directory richiede Windows.");

    public void Dispose()
    {
        // The native child lock is delete-on-close. Dispose the lock
        // BEFORE its parent directory handles to release the rename veto.
        _renameFence?.Dispose();
        _renameFence = null;
        for (var index = _handles.Count - 1; index >= 0; index--)
            _handles[index].Dispose();
        _handles.Clear();
    }

    // BY_HANDLE_FILE_INFORMATION: thirteen DWORDs, with no 64-bit padding
    // between the Windows FILETIME low/high halves.
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
}
