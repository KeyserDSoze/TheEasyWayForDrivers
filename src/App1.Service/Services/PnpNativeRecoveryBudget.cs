namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Controls optional same-instance, read-only SetupAPI lookups in one
/// Win32_PnPEntity inventory scan. CompatibleID-only enrichment is useful,
/// but must not exhaust all native reads before a late NVIDIA/Killer/audio
/// device with missing or generic HardwareID can be inspected.
/// This only reserves lookup attempts; it never fabricates an ID.
/// </summary>
public sealed class PnpNativeRecoveryBudget
{
    public const int MaximumLookups = 2048;
    public const int HardwareRecoveryReserve = 256;
    // Enriching an already specific VEN+DEV with a possible OEM SUBSYS is
    // lower priority than recovering an actually missing HardwareID.
    // Keep a bounded optional share and NEVER use the hardware reserve.
    public const int MaximumOemSubsystemOnlyLookups = 96;
    public const int MaximumCompatibleOnlyLookups =
        MaximumLookups - HardwareRecoveryReserve;

    private int _used;
    private int _compatibleOnlyUsed;
    private int _oemSubsystemOnlyUsed;

    public int OemSubsystemOnlyUsed => _oemSubsystemOnlyUsed;
    public int Used => _used;
    public int CompatibleOnlyUsed => _compatibleOnlyUsed;
    public int Remaining => MaximumLookups - _used;

    /// <summary>
    /// Admit one native lookup for the same already-known PnP instance.
    /// A request with missing/incomplete HardwareID has priority; requests
    /// solely for absent CompatibleID may not consume the 256 reserved slots.
    /// Both types still share the unchanged absolute 2048-attempt limit.
    /// </summary>
    public bool TryAcquire(
        bool needsHardware, bool needsCompatible,
        bool oemSubsystemOnly = false)
    {
        if ((!needsHardware && !needsCompatible) ||
            _used >= MaximumLookups)
            return false;

        if (oemSubsystemOnly)
        {
            // Optional SUBSYS enrichment must not exhaust the reserved
            // 256 hardware-recovery attempts for late missing devices.
            if (!needsHardware ||
                _oemSubsystemOnlyUsed >= MaximumOemSubsystemOnlyLookups ||
                _used >= MaximumCompatibleOnlyLookups)
                return false;
            _oemSubsystemOnlyUsed++;
            _used++;
            return true;
        }

        if (!needsHardware)
        {
            // Always leave the final 256 attempts for hardware-model
            // recovery, even if some earlier attempts already retrieved
            // hardware. Compatible-only reads stop at total use 1792.
            if (_used >= MaximumCompatibleOnlyLookups)
                return false;
            _compatibleOnlyUsed++;
        }

        _used++;
        return true;
    }
}
