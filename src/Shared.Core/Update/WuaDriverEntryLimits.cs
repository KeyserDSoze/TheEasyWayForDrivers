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
    // Preserve the original first 2048 COM entries, plus up to 512
    // evenly spaced read-only probes in the remaining WUA collection.
    // The 1024-ID IPC/association ceiling above is NOT increased.
    public const int SequentialEntriesToInspect = 2048;
    public const int AdditionalTailProbes = 512;
    public const int MaximumEntriesToInspect =
        SequentialEntriesToInspect + AdditionalTailProbes;
    public const int MaximumIdLength = 512;
}
