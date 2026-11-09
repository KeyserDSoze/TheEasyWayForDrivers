using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

public static class IntelHardwareDetector
{
    public static bool IsIntelDevice(DriverInfo driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        if (driver.HardwareIds
            .Concat(driver.CompatibleIds)
            .Any(IsIntelHardwareId) ||
            SwcVendorComponentIdentity.IsIntel(driver))
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
        // VEN/VID is not a model: require a validated DEV/PID and
        // allow only well-formed SUBSYS/REV/CC or REV/MI extensions.
        var model = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(value);
        return model?.StartsWith(@"PCI\VEN_8086&DEV_",
                   StringComparison.OrdinalIgnoreCase) == true ||
               model?.StartsWith(@"USB\VID_8087&PID_",
                   StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool ContainsIntel(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(
            "Intel",
            StringComparison.OrdinalIgnoreCase);
}
