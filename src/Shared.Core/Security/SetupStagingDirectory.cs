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
    /// This does not make path traversal/opens handle-relative (#529).
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
    /// Reject pre-existing staging redirects before writing payload.zip.
    /// FileMode.CreateNew also refuses to truncate any existing file/link.
    /// This path-based check is NOT atomic against concurrent swaps (#529).
    /// </summary>
    public static FileStream CreatePayloadFile(string staging)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(staging);
        var root = Path.GetFullPath(staging);
        EnsureNoReparseAncestors(root);
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
