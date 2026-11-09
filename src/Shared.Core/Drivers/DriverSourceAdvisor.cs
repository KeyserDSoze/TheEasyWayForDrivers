using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

public static class DriverSourceAdvisor
{
    private static readonly HashSet<string> OemSensitiveClasses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Firmware",
            "System",
            "SoftwareComponent",
            "Extension",
            "Battery",
            "Biometric",
            "Camera",
            "Sensor"
        };

    public static string GetSource(
        DriverInfo driver,
        IReadOnlyCollection<DriverUpdateInfo> matchedUpdates,
        string? systemOemDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(matchedUpdates);

        if (matchedUpdates.Count > 0)
        {
            return "Windows Update";
        }

        // The read-only vendor handoff has already checked strict PnP PCI
        // evidence, including conflicts with stale NVIDIA/Intel/AMD
        // provider labels. Do not override it by running brand-first
        // detectors again. For other (non-actionable) PnP classes keep the
        // historical fallback, but not against a conflicting PCI vendor.
        var supportVendor = VendorSourceDiscovery.GetRecognizedSupportVendor(driver);
        var hardwareVendor = supportVendor ??
            (NvidiaHardwareDetector.IsNvidiaDevice(driver) &&
             !VendorSourceDiscovery.HasForeignPciVendor(driver, "10DE")
                ? "NVIDIA"
                : AmdHardwareDetector.IsAmdDevice(driver) &&
                  !VendorSourceDiscovery.HasForeignPciVendor(driver, "1002")
                    ? "AMD"
                    : IntelHardwareDetector.IsIntelDevice(driver) &&
                      !VendorSourceDiscovery.HasForeignPciVendor(driver, "8086")
                        ? "Intel"
                        : null);

        if (IsKnownSystemOem(systemOemDisplayName) &&
            IsSystemSpecificDevice(driver))
        {
            return $"{systemOemDisplayName} (OEM PC)";
        }

        if (hardwareVendor is not null)
        {
            return IsKnownSystemOem(systemOemDisplayName) &&
                   IsInternalDevice(driver)
                ? $"{hardwareVendor} · {systemOemDisplayName} OEM"
                : hardwareVendor;
        }

        if (IsKnownSystemOem(systemOemDisplayName) &&
            IsInternalDevice(driver))
        {
            return $"{systemOemDisplayName} (OEM PC)";
        }

        return "Windows / OEM";
    }

    private static bool IsSystemSpecificDevice(DriverInfo driver) =>
        !string.IsNullOrWhiteSpace(driver.DeviceClass) &&
        OemSensitiveClasses.Contains(driver.DeviceClass);

    private static bool IsInternalDevice(DriverInfo driver) =>
        driver.HardwareIds
            .Concat(driver.CompatibleIds)
            .Any(id =>
                id.StartsWith(
                    @"PCI\",
                    StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith(
                    @"ACPI\",
                    StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith(
                    @"ROOT\",
                    StringComparison.OrdinalIgnoreCase) ||
                // Internal HD Audio/Intel SST codecs are typically tied
                // to the notebook's OEM audio topology and extensions.
                id.StartsWith(
                    @"HDAUDIO\",
                    StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith(
                    @"INTELAUDIO\",
                    StringComparison.OrdinalIgnoreCase));

    private static bool IsKnownSystemOem(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(
            value,
            "PC OEM",
            StringComparison.OrdinalIgnoreCase);
}
