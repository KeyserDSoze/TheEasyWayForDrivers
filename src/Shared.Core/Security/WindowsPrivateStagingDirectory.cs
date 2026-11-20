using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Creates an installer staging directory with an explicit protected DACL
/// via NtCreateFile relative to the pinned Temp handle (#586), avoiding an
/// inherited-ACL or pathname-resolution exposure window. Verification reads
/// owner/DACL on the same freshly-created native handle (#572/#586).
/// Neither these ACLs nor randomized names eliminate every TOCTOU (#529).
/// </summary>
internal static class WindowsPrivateStagingDirectory
{
    private const int MaxAttempts = 10;
    private const int ErrorInsufficientBuffer = 122;
    private const uint OwnerSecurityInformation = 0x00000001;
    private const uint DaclSecurityInformation = 0x00000004;
    private const int FileAllAccessMask = 0x001F01FF;
    private const int MaxSecurityDescriptorBytes = 64 * 1024;

    private const string SystemSid = "S-1-5-18";
    private const string AdministratorsSid = "S-1-5-32-544";

    public static string Create(string tempRoot, string prefix) =>
        CreateCore(tempRoot, prefix, afterCreatedForTests: null);

    // Internal test-only seam while the new leaf's native handle is held.
    // Production never calls the observer (#586).
    internal static string CreateWithAfterCreatedForTests(
        string tempRoot, string prefix, Action<string> afterCreated)
    {
        ArgumentNullException.ThrowIfNull(afterCreated);
        return CreateCore(tempRoot, prefix, afterCreated);
    }

    private static string CreateCore(
        string tempRoot, string prefix, Action<string>? afterCreatedForTests)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(tempRoot));
        // Reject Windows network/device namespaces for privileged staging.
        if (tempRoot.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException(
                "Lo staging richiede un disco locale Windows.");

        var actor = CurrentUserSid();
        // Running as LocalSystem makes actor == SYSTEM. Keep the DACL
        // canonical and unique rather than adding the same SID twice.
        var principals = new[] { actor, SystemSid, AdministratorsSid }
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var sddl = "D:P" + string.Concat(
            principals.Select(sid => $"(A;OICI;FA;;;{sid})"));

        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
            sddl, 1, out var descriptor, out _) || descriptor == IntPtr.Zero)
            throw new IOException(
                "Impossibile inizializzare la sicurezza dello staging.");

        try
        {
            // The complete parent is pinned BEFORE selecting the leaf. Its
            // verified handle, not a path string, is the native create root.
            using var parentAnchor = SetupDirectoryAnchor.Open(tempRoot);
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var leaf = prefix + Guid.NewGuid().ToString("N");
                var path = Path.Combine(tempRoot, leaf);
                SafeFileHandle created;
                try
                {
                    created = WindowsHandleRelativeFile.CreateNewPrivateDirectory(
                        parentAnchor.LeafDirectoryHandle, leaf, descriptor);
                }
                catch (WindowsHandleRelativeFile
                    .PrivateDirectoryNameCollisionException)
                {
                    continue;
                }

                using (created)
                {
                    // The OS-created object is checked, never a re-resolved
                    // pathname. A failed audit can delete ONLY this exact
                    // verified empty child relative to the pinned parent.
                    var identity =
                        WindowsHandleRelativeFile.ReadDirectoryIdentity(created);
                    try
                    {
                        afterCreatedForTests?.Invoke(path);
                        Verify(created);
                        return path;
                    }
                    catch
                    {
                        created.Dispose(); // release delete-share veto
                        try
                        {
                            WindowsHandleRelativeFile.DeleteVerifiedEmptyDirectory(
                                parentAnchor.LeafDirectoryHandle, leaf, identity);
                        }
                        catch
                        {
                            // Never replace the original validation failure.
                            // Failing closed may leave an empty private leaf.
                        }
                        throw;
                    }
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

        // The path-taking API first pins the actual filesystem object.
        // Reading the ACL later must use THIS handle, not a fresh lookup.
        using var anchor = SetupDirectoryAnchor.Open(path);
        Verify(anchor.LeafDirectoryHandle);
    }

    public static void Verify(SafeFileHandle directoryHandle)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();

        ArgumentNullException.ThrowIfNull(directoryHandle);
        if (directoryHandle.IsClosed || directoryHandle.IsInvalid)
            throw new IOException("Handle dello staging non valido.");

        // READ_CONTROL was acquired for the same leaf directory handle.
        // Do not use GetFileSecurityW(path) after traversal (#572).
        if (!GetKernelObjectSecurity(directoryHandle,
            OwnerSecurityInformation | DaclSecurityInformation,
            null, 0, out var required) &&
            Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            throw new IOException(
                "Impossibile leggere i permessi della directory di staging.");

        if (required == 0 || required > MaxSecurityDescriptorBytes)
            throw new InvalidDataException(
                "Descriptor di sicurezza staging non valido.");

        var bytes = new byte[checked((int)required)];
        if (!GetKernelObjectSecurity(directoryHandle,
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
            acl is null || acl.Count != accepted.Count)
            throw new InvalidDataException(
                "Lo staging non ha proprietario o ACL privati verificati.");

        // Count == 3 and membership alone are insufficient:
        // {user, user, SYSTEM} would pass despite missing Administrators.
        var remaining = new HashSet<string>(accepted, StringComparer.OrdinalIgnoreCase);
        foreach (GenericAce item in acl)
        {
            if (item is not CommonAce ace ||
                ace.AceType != AceType.AccessAllowed ||
                ace.AccessMask != FileAllAccessMask ||
                ace.SecurityIdentifier is null ||
                !remaining.Remove(ace.SecurityIdentifier.Value) ||
                ace.AceFlags != (AceFlags.ObjectInherit | AceFlags.ContainerInherit))
                throw new InvalidDataException(
                    "Lo staging contiene autorizzazioni inattese.");
        }

        if (remaining.Count != 0)
            throw new InvalidDataException(
                "Lo staging non contiene tutte le autorizzazioni richieste.");
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

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode,
        EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string stringSecurityDescriptor, uint revision,
        out IntPtr securityDescriptor, out uint size);

    [DllImport("advapi32.dll", EntryPoint = "GetKernelObjectSecurity",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKernelObjectSecurity(
        SafeFileHandle handle, uint requestedInformation,
        [Out] byte[]? securityDescriptor, uint length,
        out uint lengthNeeded);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
