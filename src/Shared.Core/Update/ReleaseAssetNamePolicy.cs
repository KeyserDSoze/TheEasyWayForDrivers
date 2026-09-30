namespace TheEasyWayForDrivers.Core.Update;

public static class ReleaseAssetNamePolicy
{
    public static IReadOnlyList<string> PreferredSetupAssetNames { get; } =
    [
        "OmegaDrive-Setup.exe",
        "TheEasyWayForDrivers-Setup.exe"
    ];
}
