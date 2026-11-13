using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Single device-row presentation policy for both pre-search inventory
/// status and post-search WUA hints. An unavailable Win32_PnPSignedDriver
/// query does not prove that a driver is missing, but equally must NOT
/// be shown as a green, healthy device.
/// This policy does not determine installation eligibility: WUA actions
/// remain separately gated by DeviceWuaPresentationGate/QuickUpdatePolicy.
/// </summary>
public static class DriverDevicePresentationPolicy
{
    public static bool NeedsAttention(
        DriverInfo driver, bool hasOrdinaryWuaUpdate)
    {
        ArgumentNullException.ThrowIfNull(driver);

        // DriverInfo.NeedsAttention deliberately excludes some unverified
        // WMI inventory states to avoid diagnosing a missing driver. The
        // *UI review queue* must include them without labeling them faulty.
        return driver.NeedsAttention || driver.IsDriverUnverified ||
            hasOrdinaryWuaUpdate;
    }

    public static string Status(
        DriverInfo driver,
        bool hasOrdinaryWuaUpdate,
        bool hasReviewOnlyWuaHint)
    {
        ArgumentNullException.ThrowIfNull(driver);

        // Windows PnP authority and unverified local metadata take
        // precedence over optional update hints, so a device requiring
        // verification never gets a misleading OK/Available badge.
        if (driver.NeedsAttention || driver.IsDriverUnverified)
            return driver.Status;

        if (hasOrdinaryWuaUpdate)
            return "Aggiornamento disponibile";
        if (hasReviewOnlyWuaHint)
            return "WUA da verificare";
        return "OK";
    }
}
