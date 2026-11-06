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

        // Prefer the recognized network/audio chip vendor over broad
        // driver-provider labels (e.g. Intel SST hosting a Realtek codec).
        var supportVendor = VendorSourceDiscovery.GetRecognizedSupportVendor(driver);
        var hardwareVendor = supportVendor is
            "Realtek" or "Broadcom" or "Qualcomm / Atheros" or "MediaTek"
            ? supportVendor
            : NvidiaHardwareDetector.IsNvidiaDevice(driver)
                ? "NVIDIA"
                : AmdHardwareDetector.IsAmdDevice(driver)
                    ? "AMD"
                    : IntelHardwareDetector.IsIntelDevice(driver)
                        ? "Intel"
                        : supportVendor;

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
