using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Windows defense-in-depth for the installer staging directory (#562).
/// Opens the local volume root once, then each existing ancestor relative
/// to the verified parent handle, without FILE_SHARE_DELETE (#571).
/// An exclusive, delete-on-close leaf lock file
/// also prevents folder rename on Windows (which handle sharing alone did
/// not block in the initial #562 CI test). The lock is created relative to
/// the VERIFIED leaf directory handle, never via a fresh path lookup (#569).
/// Parent handles are held before children and checked for reparse attributes.
/// Handle-relative ZIP directories and files remain supported (#568/#567);
/// the final root handle carries READ_CONTROL for ACL verification (#572).
/// Every parent-child identity is revalidated after placing the leaf
/// lock to reject observed swaps before returning (#573).
/// The volume-root opening and broader TOCTOU QA stay in #529.
/// </summary>
public sealed class SetupDirectoryAnchor : IDisposable
{
    private const uint FileReadAttributes = 0x80;
    private const uint FileTraverse = 0x20;
    private const uint FileAddFile = 0x02;
    private const uint FileAddSubdirectory = 0x04;
    private const uint ReadControl = 0x00020000;
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

    public static SetupDirectoryAnchor Open(string directory) =>
        OpenCore(directory, afterVerifiedComponent: null);

    // Internal synchronization seam for Windows regression tests only.
    // Production callers always use Open(), without a callback. This
    // permits a precise swap between validation and the next native open.
    internal static SetupDirectoryAnchor OpenWithValidationObserverForTests(
        string directory, Action<string> afterVerifiedComponent)
    {
        ArgumentNullException.ThrowIfNull(afterVerifiedComponent);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        return OpenCore(directory, afterVerifiedComponent);
    }

    private static SetupDirectoryAnchor OpenCore(
        string directory, Action<string>? afterVerifiedComponent)
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
            // The local volume root is the sole pathname-based opening.
            // Every descendant uses NtCreateFile relative to the already
            // verified parent handle, instead of resolving full paths (#571).
            var volumeRoot = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(volumeRoot))
                throw new InvalidDataException(
                    "Radice dello staging Windows non valida.");

            var relative = fullPath[volumeRoot.Length..];
            var components = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            var rootAccess = FileReadAttributes | FileTraverse |
                (components.Length == 0
                    ? FileAddFile | FileAddSubdirectory | ReadControl : 0u);
            var rootHandle = CreateFileW(volumeRoot, rootAccess,
                FileShareReadWrite, IntPtr.Zero, OpenExisting,
                BackupSemantics | OpenReparsePoint, IntPtr.Zero);
            if (rootHandle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                rootHandle.Dispose();
                throw new IOException(
                    $"Impossibile vincolare il volume (Win32 {error}).");
            }

            anchor._handles.Add(rootHandle);
            VerifyDirectoryHandle(rootHandle);

            for (var index = 0; index < components.Length; index++)
            {
                // FILE_OPEN fails closed on missing ancestors. No symlink
                // or junction is followed and there is no path fallback.
                var handle = WindowsHandleRelativeFile.OpenExistingDirectory(
                    anchor.LeafDirectoryHandle, components[index],
                    isLeaf: index == components.Length - 1);
                anchor._handles.Add(handle);
                VerifyDirectoryHandle(handle);
                afterVerifiedComponent?.Invoke(components[index]);
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

            // A concurrent rename between validating a child and placing
            // the leaf lock can move that leaf, leaving the original path
            // naming a different object. Verify every parent/child link
            // AGAIN using the already-held parent handles before returning.
            // A mismatch fails closed and Dispose deletes the leaf lock.
            for (var index = 0; index < components.Length; index++)
            {
                using var reopened = WindowsHandleRelativeFile
                    .OpenExistingDirectory(anchor._handles[index],
                        components[index],
                        isLeaf: index == components.Length - 1);
                VerifyDirectoryHandle(reopened);
                VerifySameDirectoryIdentity(anchor._handles[index + 1],
                    reopened);
            }
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
            VerifyDirectoryHandle(child);

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

    private static void VerifyDirectoryHandle(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException(
                "Impossibile verificare l'identità della directory di staging.");

        if ((info.FileAttributes & ReparsePointAttribute) != 0 ||
            (info.FileAttributes & DirectoryAttribute) == 0)
            throw new InvalidDataException(
                "Lo staging contiene un collegamento o una directory non valida.");
    }

    private static void VerifySameDirectoryIdentity(
        SafeFileHandle expected, SafeFileHandle current)
    {
        if (!GetFileInformationByHandle(expected, out var original) ||
            !GetFileInformationByHandle(current, out var reopened))
            throw new IOException(
                "Impossibile verificare la continuità dello staging.");

        // Directory IDs are stable for the life of an open handle; a path
        // swap or redirect must not be confused with the original object.
        if (original.VolumeSerialNumber != reopened.VolumeSerialNumber ||
            original.FileIndexHigh != reopened.FileIndexHigh ||
            original.FileIndexLow != reopened.FileIndexLow)
            throw new InvalidDataException(
                "La directory di staging è cambiata durante la verifica.");
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
