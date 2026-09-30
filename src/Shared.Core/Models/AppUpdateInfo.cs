namespace TheEasyWayForDrivers.Core.Models;

public sealed record AppUpdateInfo(
    Version CurrentVersion,
    Version LatestVersion,
    string TagName,
    string DownloadUrl,
    string Sha256Digest,
    string? ReleaseNotes)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
}
