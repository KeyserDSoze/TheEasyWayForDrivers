namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Shared, bounded cap for per-update WUA IWindowsDriverUpdate4 entry IDs.
/// This is device-correlation evidence, never additional update offers.
/// All service read, cross-source merge and client matching paths MUST use
/// the same cap so a device beyond entry 128 isn't silently lost downstream.
/// </summary>
public static class WuaDriverEntryLimits
{
    public const int MaximumAdditionalIds = 512;
    public const int MaximumIdLength = 512;
}
