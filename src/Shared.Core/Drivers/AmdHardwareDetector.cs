using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

public static class AmdHardwareDetector
{
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
        return OemCompanionDeviceCoverage.Count(
            drivers, PnpKnownVendorIdentity.Vendor.Amd, IsAmdDevice);
    }

    private static bool IsAmdHardwareId(string? value)
    {
        // Do not recognize a bare VEN_1002 or an invented suffix as a
        // GPU. Accept only a complete public PCI VEN+DEV model.
        var model = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(value);
        return model?.StartsWith(@"PCI\VEN_1002&DEV_",
            StringComparison.OrdinalIgnoreCase) == true;
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
