using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Windows-only opening/creation of a single file or directory child relative
/// to an existing verified directory handle (#566-#574). A child is always
/// a single validated name; root descendants use relative opens as well.
/// The volume-root open and broader installer TOCTOU QA remain in #529.
/// </summary>
internal static class WindowsHandleRelativeFile
{
    private const uint FileReadData = 0x00000001;
    private const uint FileWriteData = 0x00000002;
    private const uint FileAddFile = 0x00000002;
    private const uint FileAddSubdirectory = 0x00000004;
    private const uint FileTraverse = 0x00000020;
    private const uint ReadControl = 0x00020000;
    private const uint Delete = 0x00010000;
    private const uint FileReadAttributes = 0x00000080;
    private const uint Synchronize = 0x00100000;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileOpen = 0x00000001;
    private const uint FileOpenIf = 0x00000003;
    private const uint FileDirectoryFile = 0x00000001;
    private const uint FileDeleteOnClose = 0x00001000;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint FileCreate = 0x00000002;
    private const uint FileNonDirectoryFile = 0x00000040;
    private const uint ReparsePointAttribute = 0x00000400;
    private const uint DirectoryAttribute = 0x00000010;
    private const int FileAttributeTagInfoClass = 9;
    private const int FileDispositionInfoClass = 4;
    private const uint FileSynchronousIoNonalert = 0x00000020;
    private const uint FileOpenReparsePoint = 0x00200000;
    private const uint ObjCaseInsensitive = 0x00000040;
    private const uint ObjDontReparse = 0x00001000;

    public static FileStream CreateNew(SafeFileHandle parent, string component)
    {
        var handle = OpenNative(parent, component,
            FileWriteData | FileReadAttributes | Synchronize,
            FileAttributeNormal, 0, FileCreate,
            FileNonDirectoryFile | FileSynchronousIoNonalert |
                FileOpenReparsePoint);
        try
        {
            return new FileStream(handle, FileAccess.Write, 81920);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Open the previously staged payload ZIP for reading, using precisely
    /// the given pinned root handle. Do not look the full path up again.
    /// Check the opened object BEFORE requesting deletion on close (#574).
    /// Deletion is tied to the same handle, not to a later pathname lookup.
    /// </summary>
    internal static FileStream OpenReadPayloadAndDeleteOnClose(
        SafeFileHandle parent)
    {
        var handle = OpenNative(parent, "payload.zip",
            FileReadData | FileReadAttributes | Delete | Synchronize,
            FileAttributeNormal, 0, FileOpen,
            FileNonDirectoryFile | FileSynchronousIoNonalert |
                FileOpenReparsePoint);
        try
        {
            // FILE_OPEN_REPARSE_POINT means a redirect is opened as an
            // object, not followed. Never read or delete such an object.
            if (!GetFileInformationByHandleEx(handle,
                    FileAttributeTagInfoClass, out var info,
                    (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
                throw new IOException(
                    "Impossibile verificare il payload temporaneo.");
            if ((info.FileAttributes &
                    (ReparsePointAttribute | DirectoryAttribute)) != 0)
                throw new InvalidDataException(
                    "Il payload temporaneo non è un file regolare.");

            // Mark the verified file for deletion by HANDLE. The file is
            // only unlinked when the FileStream is disposed; on exception
            // it is also cleaned up without reopening any directory path.
            var disposition = new FileDispositionInfo { DeleteFile = 1 };
            if (!SetFileInformationByHandle(handle,
                    FileDispositionInfoClass, ref disposition,
                    (uint)Marshal.SizeOf<FileDispositionInfo>()))
                throw new IOException(
                    "Impossibile predisporre la pulizia del payload.");

            return new FileStream(handle, FileAccess.Read, 81920);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Open or create one directory component, relative to a pinned parent.
    /// The directory handle is verified separately before any descendants
    /// are opened; no path-based fallback is allowed on Windows (#568).
    /// </summary>
    internal static SafeFileHandle OpenOrCreateDirectory(
        SafeFileHandle parent, string component) =>
        OpenNative(parent, component,
            FileReadAttributes | FileTraverse | FileAddFile |
                FileAddSubdirectory | Synchronize,
            FileAttributeDirectory, FileShareReadWrite, FileOpenIf,
            FileDirectoryFile | FileSynchronousIoNonalert |
                FileOpenReparsePoint);

    /// <summary>
    /// Open one existing directory component relative to a verified parent.
    /// FILE_OPEN cannot create missing ancestors. Only the final staging
    /// leaf requests creation and READ_CONTROL rights for its rename-fence
    /// lock and handle-based owner/DACL verification (#571/#572).
    /// </summary>
    internal static SafeFileHandle OpenExistingDirectory(
        SafeFileHandle parent, string component, bool isLeaf) =>
        OpenNative(parent, component,
            FileReadAttributes | FileTraverse | Synchronize |
                (isLeaf ? FileAddFile | FileAddSubdirectory | ReadControl : 0u),
            FileAttributeDirectory, FileShareReadWrite, FileOpen,
            FileDirectoryFile | FileSynchronousIoNonalert |
                FileOpenReparsePoint);

    /// <summary>
    /// Hold an exclusive, delete-on-close lock inside the already-open
    /// child directory. Creating the lock relative to that handle avoids
    /// a path-swap window between opening a child and pinning its name.
    /// </summary>
    internal static SafeFileHandle CreateDeleteOnCloseLock(
        SafeFileHandle parent, string component) =>
        OpenNative(parent, component,
            FileWriteData | FileReadAttributes | Delete | Synchronize,
            FileAttributeNormal, 0, FileCreate,
            FileNonDirectoryFile | FileSynchronousIoNonalert |
                FileOpenReparsePoint | FileDeleteOnClose);

    private static SafeFileHandle OpenNative(
        SafeFileHandle parent, string component, uint desiredAccess,
        uint fileAttributes, uint shareAccess, uint createDisposition,
        uint createOptions)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.IsInvalid || parent.IsClosed)
            throw new IOException("Handle dello staging non disponibile.");

        // RootDirectory must refer to exactly ONE ordinary path component:
        // refuse separators, ADS, device names and other NT ambiguities.
        if (string.IsNullOrEmpty(component) || component.Length > 255 ||
            component is "." or ".." ||
            component.StartsWith(' ') || component.EndsWith(' ') ||
            component.EndsWith('.') || component.Any(char.IsControl) ||
            component.IndexOfAny(['\\', '/', ':', '<', '>', '"', '|', '?', '*']) >= 0)
            throw new InvalidDataException(
                "Nome temporaneo non consentito.");

        var baseName = component.Split('.')[0];
        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (baseName.Length == 4 &&
                (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                baseName[3] is >= '1' and <= '9'))
            throw new InvalidDataException(
                "Nome temporaneo non consentito.");

        var name = Marshal.StringToHGlobalUni(component);
        var nativeName = IntPtr.Zero;
        try
        {
            var unicode = new NativeUnicodeString
            {
                Length = checked((ushort)(component.Length * sizeof(char))),
                MaximumLength = checked((ushort)((component.Length + 1) * sizeof(char))),
                Buffer = name
            };
            nativeName = Marshal.AllocHGlobal(Marshal.SizeOf<NativeUnicodeString>());
            Marshal.StructureToPtr(unicode, nativeName, false);
            var attributes = new NativeObjectAttributes
            {
                Length = (uint)Marshal.SizeOf<NativeObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = nativeName,
                Attributes = ObjCaseInsensitive | ObjDontReparse
            };

            // OBJ_DONT_REPARSE and FILE_OPEN_REPARSE_POINT prevent following
            // a redirect. A returned directory handle is still checked
            // for REPARSE_POINT by SetupDirectoryAnchor (#568).
            var status = NtCreateFile(out var handle, desiredAccess,
                ref attributes, out _, IntPtr.Zero, fileAttributes,
                shareAccess, createDisposition, createOptions,
                IntPtr.Zero, 0);
            GC.KeepAlive(parent);
            if (status < 0 || handle.IsInvalid)
            {
                handle.Dispose();
                throw new IOException(
                    $"Operazione temporanea non riuscita (NTSTATUS 0x{status:X8}).");
            }
            return handle;
        }
        finally
        {
            if (nativeName != IntPtr.Zero)
                Marshal.FreeHGlobal(nativeName);
            Marshal.FreeHGlobal(name);
        }
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo
    {
        public byte DeleteFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle, int fileInformationClass,
        out FileAttributeTagInfo information, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle handle, int fileInformationClass,
        ref FileDispositionInfo information, uint bufferSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeUnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeObjectAttributes
    {
        public uint Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeIoStatusBlock
    {
        public IntPtr Status;
        public UIntPtr Information;
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtCreateFile(
        out SafeFileHandle handle, uint desiredAccess,
        ref NativeObjectAttributes objectAttributes,
        out NativeIoStatusBlock ioStatusBlock, IntPtr allocationSize,
        uint fileAttributes, uint shareAccess, uint createDisposition,
        uint createOptions, IntPtr eaBuffer, uint eaLength);
}
