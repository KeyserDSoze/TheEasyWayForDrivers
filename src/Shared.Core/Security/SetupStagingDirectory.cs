namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Allocates the elevated installer's scratch directory as a unique,
/// immediate child of the current account's Temp directory.
/// An older fixed shared parent could be redirected before ZIP preflight.
/// Windows creates the staging root with a protected, explicit DACL
/// (#563); SetupDirectoryAnchor pins ancestors while in use (#562).
/// File creation is still path-based; concurrent swaps remain in #529.
/// </summary>
public static class SetupStagingDirectory
{
    public const string Prefix = "OmegaDrive-Setup-";

    public static string Create()
    {
        // CreateTempSubdirectory atomically selects and creates a unique
        // path. Do not reintroduce a fixed "TheEasyWayForDrivers" parent.
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        EnsureNoReparseAncestors(tempRoot);

        // On Windows the security descriptor must exist at creation,
        // never be applied afterwards to a briefly shared directory.
        if (OperatingSystem.IsWindows())
            return WindowsPrivateStagingDirectory.Create(tempRoot, Prefix);

        var directory = Directory.CreateTempSubdirectory(Prefix);
        EnsureNoReparseAncestors(directory.FullName);
        return directory.FullName;
    }
    
    /// <summary>
    /// Check owner and protected Windows DACL before elevated setup work.
    /// The path-taking entry point anchors the object before checking its ACL
    /// via the handle (#572). Further installation TOCTOU work remains #529.
    /// </summary>
    public static void VerifyPrivateWindowsRoot(string staging)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(staging);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "La verifica ACL richiede Windows.");
        WindowsPrivateStagingDirectory.Verify(staging);
    }

    /// <summary>
    /// Inspect owner/DACL on the exact already-anchored staging directory.
    /// The elevated setup must not verify a new pathname resolution (#572).
    /// </summary>
    public static void VerifyPrivateWindowsRoot(SetupDirectoryAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "La verifica ACL richiede Windows.");
        WindowsPrivateStagingDirectory.Verify(anchor.LeafDirectoryHandle);
    }

    /// <summary>
    /// Reject pre-existing redirects. Windows opens the new file relative
    /// to the pinned staging directory handle (#566); non-Windows test hosts
    /// keep the existing CreateNew path. Other setup I/O still needs #529.
    /// </summary>
    public static FileStream CreatePayloadFile(string staging)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(staging);
        var root = Path.GetFullPath(staging);
        EnsureNoReparseAncestors(root);
        if (OperatingSystem.IsWindows())
        {
            // The root is already ACL-protected (#563); create exactly one
            // file relative to the pinned directory handle. Do not re-resolve
            // the absolute path for the privileged initial ZIP write (#566).
            using var anchor = SetupDirectoryAnchor.Open(root);
            return WindowsHandleRelativeFile.CreateNew(
                anchor.LeafDirectoryHandle, "payload.zip");
        }
        return new FileStream(Path.Combine(root, "payload.zip"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    /// <summary>
    /// Inspect existing ancestors without materializing missing components.
    /// Unexpected filesystem errors fail closed.
    /// </summary>
    public static void EnsureNoReparseAncestors(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        for (DirectoryInfo? dir = new(Path.GetFullPath(path));
             dir is not null; dir = dir.Parent)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(dir.FullName);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "La destinazione temporanea contiene un collegamento.");
        }
    }

}
