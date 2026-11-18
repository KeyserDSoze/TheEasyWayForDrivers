namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Allocates the elevated installer's scratch directory as a unique,
/// immediate child of the current account's Temp directory.
/// An older fixed shared parent could be redirected before ZIP preflight.
/// SetupDirectoryAnchor now pins existing ancestors with Win32 handles
/// while in use, but does not provide handle-relative writes or private
/// ACL protection (#529).
/// </summary>
public static class SetupStagingDirectory
{
    public const string Prefix = "OmegaDrive-Setup-";

    public static string Create()
    {
        // CreateTempSubdirectory atomically selects and creates a unique
        // path. Do not reintroduce a fixed "TheEasyWayForDrivers" parent.
        EnsureNoReparseAncestors(Path.GetFullPath(Path.GetTempPath()));
        var directory = Directory.CreateTempSubdirectory(Prefix);
        EnsureNoReparseAncestors(directory.FullName);
        return directory.FullName;
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
