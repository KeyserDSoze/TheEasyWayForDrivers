using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

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

        // A caller-supplied WUA list is not evidence for THIS device on
        // its own. Require both an ordinary WUA GUID and a strict Windows
        // Hardware/Compatible ID association before recommending WUA.
        // Other offers, including advanced/firmware/unknown-origin records,
        // must keep the OEM/chip-vendor fallback.
        var eligibleIds = QuickUpdatePolicy.AllOrdinaryIds(matchedUpdates)
            .Select(Guid.Parse)
            .ToHashSet();
        if (matchedUpdates.Any(update =>
            QuickUpdatePolicy.IsEligible(update) &&
            eligibleIds.Contains(Guid.Parse(update.Id)) &&
            DriverUpdateMatcher.MatchStrength(driver, update) > 0))
        {
            return "Windows Update";
        }

        // OEM handoffs check authoritative PCI/USB/HDAUDIO/INTELAUDIO/
        // SWC/ACPI vendor models. If a known primary model was found but
        // the handoff declined it (ineligible class, conflicting models),
        // NEVER rebrand it from a stale provider/name in this fallback.
        // A missing/unsupported PnP identity keeps the legacy fallback.
        var supportVendor = VendorSourceDiscovery.GetRecognizedSupportVendor(driver);
        var authoritativeVendor = PnpKnownVendorIdentity.FromDriver(driver);
        var hardwareVendor = supportVendor ??
            (authoritativeVendor is not null
                ? null
                : NvidiaHardwareDetector.IsNvidiaDevice(driver) &&
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
