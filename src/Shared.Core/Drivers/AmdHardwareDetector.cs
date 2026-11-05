using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

public static class AmdHardwareDetector
{
    private const string AmdPciMarker = @"PCI\VEN_1002";

    public static bool IsAmdDevice(DriverInfo driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        if (driver.HardwareIds
            .Concat(driver.CompatibleIds)
            .Any(IsAmdHardwareId))
        {
            return true;
        }

        return ContainsAmd(driver.Manufacturer) ||
               ContainsAmd(driver.DriverProvider);
    }

    public static int CountAmdDevices(
        IEnumerable<DriverInfo> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        return drivers.Count(IsAmdDevice);
    }

    private static bool IsAmdHardwareId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var id = value.Trim();
        return id.StartsWith(AmdPciMarker, StringComparison.OrdinalIgnoreCase) &&
            (id.Length == AmdPciMarker.Length ||
             id[AmdPciMarker.Length] is '&' or '\\');
    }

    private static bool ContainsAmd(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains(
                   "Advanced Micro Devices",
                   StringComparison.OrdinalIgnoreCase) ||
               value.Contains(
                   "AMD",
                   StringComparison.OrdinalIgnoreCase) ||
               value.Contains(
                   "Radeon",
                   StringComparison.OrdinalIgnoreCase);
    }
}
