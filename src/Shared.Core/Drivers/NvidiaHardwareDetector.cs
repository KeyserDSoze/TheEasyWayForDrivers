using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

public static class NvidiaHardwareDetector
{
    private const string NvidiaPciMarker = @"PCI\VEN_10DE";

    public static bool IsNvidiaDevice(DriverInfo driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        if (driver.HardwareIds
            .Concat(driver.CompatibleIds)
            .Any(IsNvidiaHardwareId))
        {
            return true;
        }

        return ContainsNvidia(driver.Manufacturer) ||
               ContainsNvidia(driver.DriverProvider);
    }

    public static int CountNvidiaDevices(
        IEnumerable<DriverInfo> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        return drivers.Count(IsNvidiaDevice);
    }

    private static bool IsNvidiaHardwareId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().StartsWith(
            NvidiaPciMarker,
            StringComparison.OrdinalIgnoreCase);

    private static bool ContainsNvidia(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(
            "NVIDIA",
            StringComparison.OrdinalIgnoreCase);
}
