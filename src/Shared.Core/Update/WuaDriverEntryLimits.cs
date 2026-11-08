namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Shared, bounded cap for per-update WUA IWindowsDriverUpdate4 entry IDs.
/// This is device-correlation evidence, never additional update offers.
/// All service read, cross-source merge and client matching paths MUST use
/// a bounded ID count so a device beyond entry 512 isn't silently lost downstream.
/// </summary>
public static class WuaDriverEntryLimits
{
    // IPC and UI correlation: bounded unique PnP IDs per WUA update.
    public const int MaximumAdditionalIds = 1024;
    // A package may repeat the same ID across WUA entries. Scan farther
    // (without transmitting unbounded lists) to find late distinct IDs.
    public const int MaximumEntriesToInspect = 2048;
    public const int MaximumIdLength = 512;
}
