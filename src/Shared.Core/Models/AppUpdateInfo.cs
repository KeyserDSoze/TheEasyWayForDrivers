namespace TheEasyWayForDrivers.Core.Models;

public sealed record AppUpdateInfo(
    Version CurrentVersion,
    Version LatestVersion,
    string TagName,
    string DownloadUrl,
    string? ReleaseNotes)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
}
