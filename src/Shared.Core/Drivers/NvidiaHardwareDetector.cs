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

    private static bool IsNvidiaHardwareId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var id = value.Trim();
        if (id.StartsWith(NvidiaPciMarker + "&", StringComparison.OrdinalIgnoreCase))
            return true;

        // NVIDIA HD Audio and laptop Platform Controllers can have generic
        // Windows device/provider names before the vendor driver is installed.
        if (id.StartsWith(@"ACPI\NVDA", StringComparison.OrdinalIgnoreCase) &&
            id.Length > @"ACPI\NVDA".Length)
            return true;

        if (!id.StartsWith(@"HDAUDIO\", StringComparison.OrdinalIgnoreCase))
            return false;

        return id[@"HDAUDIO\".Length..]
            .Split('&')
            .Any(part => string.Equals(part, "VEN_10DE", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsNvidia(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(
            "NVIDIA",
            StringComparison.OrdinalIgnoreCase);
}
