using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

public static class IntelHardwareDetector
{
    private static readonly string[] HardwareMarkers =
    [
        @"PCI\VEN_8086",
        @"USB\VID_8087"
    ];

    public static bool IsIntelDevice(DriverInfo driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        if (driver.HardwareIds
            .Concat(driver.CompatibleIds)
            .Any(IsIntelHardwareId))
        {
            return true;
        }

        return ContainsIntel(driver.Manufacturer) ||
               ContainsIntel(driver.DriverProvider);
    }

    public static int CountIntelDevices(
        IEnumerable<DriverInfo> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        return drivers.Count(IsIntelDevice);
    }

    private static bool IsIntelHardwareId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();

        return HardwareMarkers.Any(marker =>
            normalized.StartsWith(
                marker,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsIntel(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(
            "Intel",
            StringComparison.OrdinalIgnoreCase);
}
