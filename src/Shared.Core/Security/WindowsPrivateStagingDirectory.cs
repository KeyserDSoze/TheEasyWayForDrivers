using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Creates an installer staging directory with an explicit protected DACL
/// in CreateDirectoryW itself, avoiding an inherited-ACL exposure window.
/// Neither this ACL nor randomized names eliminate path-based TOCTOU (#529).
/// </summary>
internal static class WindowsPrivateStagingDirectory
{
    private const int MaxAttempts = 10;
    private const int ErrorAlreadyExists = 183;
    private const int ErrorFileExists = 80;
    private const int ErrorInsufficientBuffer = 122;
    private const uint OwnerSecurityInformation = 0x00000001;
    private const uint DaclSecurityInformation = 0x00000004;
    private const int FileAllAccessMask = 0x001F01FF;
    private const int MaxSecurityDescriptorBytes = 64 * 1024;

    private const string SystemSid = "S-1-5-18";
    private const string AdministratorsSid = "S-1-5-32-544";

    public static string Create(string tempRoot, string prefix)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();

        // Reject Windows network/device namespaces for privileged staging.
        if (tempRoot.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException(
                "Lo staging richiede un disco locale Windows.");

        var actor = CurrentUserSid();
        var sddl = $"D:P(A;OICI;FA;;;{actor})" +
            "(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";

        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
            sddl, 1, out var descriptor, out _) || descriptor == IntPtr.Zero)
            throw new IOException(
                "Impossibile inizializzare la sicurezza dello staging.");

        try
        {
            var attributes = new SecurityAttributes
            {
                Length = (uint)Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
                InheritHandle = false
            };

            // Pin Temp itself while atomically creating the private leaf.
            using var parentAnchor = SetupDirectoryAnchor.Open(tempRoot);
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var path = Path.Combine(tempRoot,
                    prefix + Guid.NewGuid().ToString("N"));
                if (!CreateDirectoryW(path, ref attributes))
                {
                    var code = Marshal.GetLastWin32Error();
                    if (code is ErrorAlreadyExists or ErrorFileExists)
                        continue;
                    throw new IOException(
                        $"Impossibile creare lo staging privato (Win32 {code}).");
                }

                try
                {
                    SetupStagingDirectory.EnsureNoReparseAncestors(path);
                    Verify(path);
                    return path;
                }
                catch
                {
                    // Only the newly created, empty directory is removed.
                    // Never recursively traverse an untrusted reparse point.
                    try { Directory.Delete(path); } catch { }
                    throw;
                }
            }

            throw new IOException(
                "Impossibile allocare una directory temporanea privata.");
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    public static void Verify(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();

        var fullPath = Path.GetFullPath(path);
        SetupStagingDirectory.EnsureNoReparseAncestors(fullPath);
        if (!GetFileSecurityW(fullPath,
            OwnerSecurityInformation | DaclSecurityInformation,
            null, 0, out var required) &&
            Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            throw new IOException(
                "Impossibile leggere i permessi della directory di staging.");

        if (required == 0 || required > MaxSecurityDescriptorBytes)
            throw new InvalidDataException(
                "Descriptor di sicurezza staging non valido.");

        var bytes = new byte[checked((int)required)];
        if (!GetFileSecurityW(fullPath,
            OwnerSecurityInformation | DaclSecurityInformation,
            bytes, (uint)bytes.Length, out _))
            throw new IOException(
                "Impossibile verificare i permessi della directory di staging.");

        var security = new RawSecurityDescriptor(bytes, 0);
        var actor = CurrentUserSid();
        var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            actor, SystemSid, AdministratorsSid
        };
        var owner = security.Owner?.Value;
        var acl = security.DiscretionaryAcl;
        if (owner is null || !accepted.Contains(owner) ||
            (security.ControlFlags & ControlFlags.DiscretionaryAclProtected) == 0 ||
            acl is null || acl.Count != 3)
            throw new InvalidDataException(
                "Lo staging non ha proprietario o ACL privati verificati.");

        foreach (GenericAce item in acl)
        {
            if (item is not CommonAce ace ||
                ace.AceType != AceType.AccessAllowed ||
                ace.AccessMask != FileAllAccessMask ||
                ace.SecurityIdentifier is null ||
                !accepted.Contains(ace.SecurityIdentifier.Value) ||
                (ace.AceFlags & AceFlags.Inherited) != 0 ||
                (ace.AceFlags & (AceFlags.ObjectInherit | AceFlags.ContainerInherit)) !=
                    (AceFlags.ObjectInherit | AceFlags.ContainerInherit))
                throw new InvalidDataException(
                    "Lo staging contiene autorizzazioni inattese.");
        }
    }

    private static string CurrentUserSid()
    {
        // Keep the platform guard local: Shared.Core also targets non-Windows
        // test hosts and treats CA1416 warnings as build errors.
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();

        return WindowsIdentity.GetCurrent().User?.Value ??
            throw new InvalidDataException(
                "Identità Windows non verificabile per lo staging.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public uint Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode,
        EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string stringSecurityDescriptor, uint revision,
        out IntPtr securityDescriptor, out uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode,
        EntryPoint = "CreateDirectoryW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(
        string pathName, ref SecurityAttributes securityAttributes);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode,
        EntryPoint = "GetFileSecurityW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileSecurityW(
        string fileName, uint requestedInformation,
        [Out] byte[]? securityDescriptor, uint length,
        out uint lengthNeeded);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
