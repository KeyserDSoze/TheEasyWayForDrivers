using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Windows-only opening/creation of a single file or directory child relative
/// to an existing verified directory handle (#566-#582). A child is always
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
    // Native FILE_INFORMATION_CLASS.FileRenameInformation (not Win32 class 3).
    private const int FileRenameInformationClass = 10;
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
    /// Create a private file with DELETE access for handle-based publication.
    /// Keep this handle OPEN during copy and FileRenameInfo; the original
    /// temp filename is never reopened for publishing or cleanup (#580).
    /// </summary>
    internal static FileStream CreateNewForCopy(
        SafeFileHandle parent, string component)
    {
        var handle = OpenNative(parent, component,
            FileWriteData | FileReadAttributes | Delete | Synchronize,
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
    /// Publish exactly the file represented by the already open handle,
    /// not an object looked up a second time by its temporary name.
    /// The destination is a single validated child of the pinned parent.
    /// The native FileRenameInformation class uses its own enumeration.
    /// </summary>
    internal static void RenameCopyByHandle(
        SafeFileHandle file, SafeFileHandle parent, string destination,
        bool overwrite)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(parent);
        if (file.IsClosed || file.IsInvalid || parent.IsClosed || parent.IsInvalid)
            throw new IOException("Handle di pubblicazione non disponibile.");
        ValidateComponent(destination);

        // FILE_RENAME_INFO: DWORD union (ReplaceIfExists), HANDLE
        // RootDirectory, DWORD FileNameLength, WCHAR FileName[N].
        // Use explicit native offsets; Marshal.SizeOf a trailing array
        // structure would include alignment padding on x64.
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var lengthOffset = rootOffset + IntPtr.Size;
        var nameOffset = lengthOffset + sizeof(uint);
        var bytes = Encoding.Unicode.GetBytes(destination);
        var size = checked(nameOffset + bytes.Length);
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, memory, size);
            Marshal.WriteInt32(memory, overwrite ? 1 : 0);
            Marshal.WriteIntPtr(memory, rootOffset,
                parent.DangerousGetHandle());
            Marshal.WriteInt32(memory, lengthOffset, bytes.Length);
            Marshal.Copy(bytes, 0, IntPtr.Add(memory, nameOffset),
                bytes.Length);

            // Use the native NtSetInformationFile contract: the Win32
            // FileRenameInfo wrapper returned ERROR_INVALID_PARAMETER
            // (87) for a non-null RootDirectory on Windows CI (#580).
            // Native FILE_RENAME_INFORMATION is class 10, not Win32 3.
            var status = NtSetInformationFile(file, out _, memory,
                (uint)size, FileRenameInformationClass);
            if (status < 0)
                throw new IOException(
                    $"Impossibile pubblicare il file verificato (NTSTATUS 0x{status:X8}).");
            GC.KeepAlive(parent);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    /// <summary>
    /// Mark precisely this original temporary file for deletion. A failed
    /// write, validation or rename cannot delete a different file at the
    /// former temp pathname (#580).
    /// </summary>
    internal static void DeleteCopyByHandleOnClose(SafeFileHandle file)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        ArgumentNullException.ThrowIfNull(file);
        if (file.IsClosed || file.IsInvalid)
            throw new IOException("Handle temporaneo già chiuso.");

        var info = new FileDispositionInfo { DeleteFile = 1 };
        if (!SetFileInformationByHandle(file, FileDispositionInfoClass,
                ref info, (uint)Marshal.SizeOf<FileDispositionInfo>()))
            throw new IOException(
                $"Impossibile eliminare il file temporaneo verificato (Win32 {Marshal.GetLastWin32Error()}).");
    }

    /// <summary>
    /// Create a single exclusive Windows handle for writing AND extracting
    /// the embedded ZIP (#575). FILE_CREATE rejects preexisting links;
    /// FILE_DELETE_ON_CLOSE cleans up even after copy/extraction failure.
    /// </summary>
    internal static FileStream CreateEphemeralReadWritePayload(
        SafeFileHandle parent)
    {
        var handle = OpenNative(parent, "payload.zip",
            FileReadData | FileWriteData | FileReadAttributes |
                Delete | Synchronize,
            FileAttributeNormal, 0, FileCreate,
            FileNonDirectoryFile | FileSynchronousIoNonalert |
                FileOpenReparsePoint | FileDeleteOnClose);
        try
        {
            return new FileStream(handle, FileAccess.ReadWrite, 81920);
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
    /// Stable identity of an already-open, verified ordinary directory.
    /// Its native file index is compared with the entry reopened from
    /// the pinned parent when the leaf's rename fence is released (#582).
    /// </summary>
    internal readonly record struct DirectoryIdentity(
        uint VolumeSerialNumber, uint FileIndexHigh, uint FileIndexLow);

    internal static DirectoryIdentity ReadDirectoryIdentity(
        SafeFileHandle directory)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        ArgumentNullException.ThrowIfNull(directory);
        if (directory.IsInvalid || directory.IsClosed ||
            !GetFileInformationByHandle(directory, out var info))
            throw new IOException(
                "Impossibile verificare l'identità della directory.");

        if ((info.FileAttributes & ReparsePointAttribute) != 0 ||
            (info.FileAttributes & DirectoryAttribute) == 0)
            throw new InvalidDataException(
                "La directory da rimuovere non è valida.");

        return new DirectoryIdentity(info.VolumeSerialNumber,
            info.FileIndexHigh, info.FileIndexLow);
    }

    /// <summary>
    /// Remove ONLY the same previously verified EMPTY directory through
    /// a fresh native FILE_OPEN relative to an anchored parent (#582).
    /// No recursive framework deletion and no name-only File.Delete.
    /// A swapped child or redirect fails without being unlinked.
    /// </summary>
    internal static void DeleteVerifiedEmptyDirectory(
        SafeFileHandle parent, string component, DirectoryIdentity expected)
    {
        using var directory = OpenNative(parent, component,
            FileReadAttributes | Delete | Synchronize,
            FileAttributeDirectory, 0, FileOpen,
            FileDirectoryFile | FileSynchronousIoNonalert |
                FileOpenReparsePoint);

        if (ReadDirectoryIdentity(directory) != expected)
            throw new InvalidDataException(
                "La directory è cambiata durante la rimozione.");

        // Windows refuses to delete nonempty directories. The identity
        // check and disposition apply to the SAME handle, not to the
        // pathname that was originally validated.
        var disposition = new FileDispositionInfo { DeleteFile = 1 };
        if (!SetFileInformationByHandle(directory, FileDispositionInfoClass,
                ref disposition, (uint)Marshal.SizeOf<FileDispositionInfo>()))
            throw new IOException(
                "Impossibile rimuovere la directory vuota verificata.");
    }

    /// <summary>
    /// Delete exactly one existing regular file beneath an anchored Windows
    /// directory. The file is opened with FILE_OPEN_REPARSE_POINT and no
    /// name is resolved after validation; delete acts on this same handle.
    /// Refuse reparse points, directories and files with hardlinks (#581).
    /// </summary>
    internal static void DeleteExistingRegularFile(
        SafeFileHandle parent, string component)
    {
        using var file = OpenNative(parent, component,
            FileReadAttributes | Delete | Synchronize,
            FileAttributeNormal, 0, FileOpen,
            FileNonDirectoryFile | FileSynchronousIoNonalert |
                FileOpenReparsePoint);

        if (!GetFileInformationByHandle(file, out var info))
            throw new IOException(
                "Impossibile verificare il file prima della rimozione.");

        if ((info.FileAttributes &
                (ReparsePointAttribute | DirectoryAttribute)) != 0 ||
            info.NumberOfLinks != 1)
            throw new InvalidDataException(
                "La rimozione contiene un file collegato o non regolare.");

        DeleteCopyByHandleOnClose(file);
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


    // Shared single-component NT name validation for open AND rename.
    private static void ValidateComponent(string component)
    {
        if (string.IsNullOrEmpty(component) || component.Length > 255 ||
            component is "." or ".." ||
            component.StartsWith(' ') || component.EndsWith(' ') ||
            component.EndsWith('.') || component.Any(char.IsControl) ||
            component.IndexOfAny(['\\', '/', ':', '<', '>', '"', '|', '?', '*']) >= 0)
            throw new InvalidDataException("Nome temporaneo non consentito.");

        var baseName = component.Split('.')[0];
        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (baseName.Length == 4 &&
                (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                baseName[3] is >= '1' and <= '9'))
            throw new InvalidDataException("Nome temporaneo non consentito.");
    }

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

        ValidateComponent(component);

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


    // BY_HANDLE_FILE_INFORMATION: thirteen DWORDs, no FILETIME padding.
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

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtSetInformationFile(
        SafeFileHandle handle, out NativeIoStatusBlock ioStatusBlock,
        IntPtr fileInformation, uint length, int fileInformationClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file, out HandleFileInformation information);

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
