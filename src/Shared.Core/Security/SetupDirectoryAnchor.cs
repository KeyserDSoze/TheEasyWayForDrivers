using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Windows defense-in-depth for the installer staging directory (#562).
/// Pins every existing ancestor from the volume root down to the target
/// without FILE_SHARE_DELETE. Parents are opened before their children,
/// and each opened handle is checked for reparse-point attributes.
/// This does not provide handle-relative file creation or private ACLs (#529).
/// </summary>
public sealed class SetupDirectoryAnchor : IDisposable
{
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareReadWrite = 0x01 | 0x02;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint DirectoryAttribute = 0x10;
    private const uint ReparsePointAttribute = 0x400;

    private readonly List<SafeFileHandle> _handles = [];

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
                var handle = CreateFileW(path, FileReadAttributes,
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

            return anchor;
        }
        catch
        {
            anchor.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        // Close in child-to-parent order, releasing directory rename locks.
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
