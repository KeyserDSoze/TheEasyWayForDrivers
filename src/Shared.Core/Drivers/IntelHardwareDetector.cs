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

    // PCI 8086 and 1A56 already identify Intel in the OEM companion and
    // known-vendor PnP paths. Require both a valid vendor model and a
    // same-instance PCI display/network class for generic WMI rows (#541).
    // A secondary Intel Compatible ID must not override a different
    // valid primary PCI/USB/SWC vendor from the same PnP instance.
    // A class-only PCI CC token is not an Intel product model.
    private static bool HasTrustedIntelPciModel(DriverInfo driver) =>
        PnpKnownVendorIdentity.FromDriver(driver) ==
            PnpKnownVendorIdentity.Vendor.Intel &&
        !VendorSourceDiscovery.HasForeignPciVendor(driver, "8086", "1A56");

    public static bool HasIntelDisplayClass(DriverInfo driver) =>
        HasTrustedIntelPciModel(driver) &&
        (PciClassIdentity.HasDisplayClass(driver, "8086") ||
         PciClassIdentity.HasDisplayClass(driver, "1A56"));

    public static bool HasIntelNetworkClass(DriverInfo driver) =>
        HasTrustedIntelPciModel(driver) &&
        (PciClassIdentity.HasNetworkClass(driver, "8086") ||
         PciClassIdentity.HasNetworkClass(driver, "1A56"));

    public static int CountIntelDevices(
        IEnumerable<DriverInfo> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        return OemCompanionDeviceCoverage.Count(
            drivers, PnpKnownVendorIdentity.Vendor.Intel, IsIntelDevice);
    }

    private static bool IsIntelHardwareId(string? value)
    {
        // VEN/VID is not a model: require a validated DEV/PID and
        // allow only well-formed SUBSYS/REV/CC or REV/MI extensions.
        var model = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(value);
        return model?.StartsWith(@"PCI\VEN_8086&DEV_",
                   StringComparison.OrdinalIgnoreCase) == true ||
               model?.StartsWith(@"PCI\VEN_1A56&DEV_",
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
