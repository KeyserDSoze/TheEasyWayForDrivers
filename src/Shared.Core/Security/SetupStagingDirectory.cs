namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Allocates the elevated installer's scratch directory as a unique,
/// immediate child of the current account's Temp directory.
/// An older fixed shared parent could be redirected before ZIP preflight.
/// This does not replace reparse validation or guarantee race-free
/// handle-relative filesystem operations (#529).
/// </summary>
public static class SetupStagingDirectory
{
    public const string Prefix = "OmegaDrive-Setup-";

    public static string Create()
    {
        // CreateTempSubdirectory atomically selects and creates a unique
        // path. Do not reintroduce a fixed "TheEasyWayForDrivers" parent.
        return Directory.CreateTempSubdirectory(Prefix).FullName;
    }
}
