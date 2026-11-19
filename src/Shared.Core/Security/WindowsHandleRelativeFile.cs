using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Windows-only native creation of a single new file relative to an open
/// directory handle. No full path is re-resolved for the file itself (#566).
/// Root traversal and later ZIP extraction are separate concerns (#529).
/// </summary>
internal static class WindowsHandleRelativeFile
{
    private const uint FileWriteData = 0x00000002;
    private const uint FileReadAttributes = 0x00000080;
    private const uint Synchronize = 0x00100000;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileCreate = 0x00000002;
    private const uint FileNonDirectoryFile = 0x00000040;
    private const uint FileSynchronousIoNonalert = 0x00000020;
    private const uint FileOpenReparsePoint = 0x00200000;
    private const uint ObjCaseInsensitive = 0x00000040;
    private const uint ObjDontReparse = 0x00001000;

    public static FileStream CreateNew(SafeFileHandle parent, string component)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.IsInvalid || parent.IsClosed)
            throw new IOException("Handle dello staging non disponibile.");

        // A native RootDirectory child must be a single, ordinary filename:
        // no path separators, device names, ADS, drive prefixes or traversal.
        if (string.IsNullOrEmpty(component) || component.Length > 255 ||
            component is "." or ".." ||
            component.StartsWith(' ') || component.EndsWith(' ') ||
            component.EndsWith('.') || component.Any(char.IsControl) ||
            component.IndexOfAny(['\\', '/', ':', '<', '>', '"', '|', '?', '*']) >= 0)
            throw new InvalidDataException(
                "Nome file temporaneo non consentito.");

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
                "Nome file temporaneo non consentito.");

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

            // FILE_CREATE is exclusive: an existing file, link or directory
            // is never replaced. FILE_OPEN_REPARSE_POINT and OBJ_DONT_REPARSE
            // refuse path redirects. On failure there is no Win32-path fallback.
            var status = NtCreateFile(out var handle,
                FileWriteData | FileReadAttributes | Synchronize,
                ref attributes, out _, IntPtr.Zero, FileAttributeNormal,
                0, FileCreate, FileNonDirectoryFile |
                    FileSynchronousIoNonalert | FileOpenReparsePoint,
                IntPtr.Zero, 0);
            GC.KeepAlive(parent);
            if (status < 0 || handle.IsInvalid)
            {
                handle.Dispose();
                throw new IOException(
                    $"Creazione relativa del payload non riuscita (NTSTATUS 0x{status:X8}).");
            }

            try
            {
                // FileStream owns the returned NT handle. The parent may be
                // disposed after the syscall: its identity was used atomically.
                return new FileStream(handle, FileAccess.Write, 81920);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
        finally
        {
            if (nativeName != IntPtr.Zero)
                Marshal.FreeHGlobal(nativeName);
            Marshal.FreeHGlobal(name);
        }
    }

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
