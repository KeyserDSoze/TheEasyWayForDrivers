using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

public static class NvidiaHardwareDetector
{
    private const string NvidiaPciModelPrefix = @"PCI\VEN_10DE&DEV_";
    private const string NvidiaAudioVendor = @"HDAUDIO\FUNC_01&VEN_10DE";

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

    /// <summary>
    /// Hardware-only NVIDIA Platform/DCH identity, independent of the
    /// friendly Windows name or installed driver provider. This is a device
    /// classification hint, never evidence of an available driver update.
    /// </summary>
    public static bool HasNvidiaPlatformId(DriverInfo driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        return driver.HardwareIds.Concat(driver.CompatibleIds)
            .Any(IsNvidiaPlatformId);
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

        // A bare VEN_10DE or VEN_10DE&FAKE token identifies no PCI
        // device model. Keep strict checks in sync with WUA's reserved-ID
        // selection, without promoting any evidence to an OEM update.
        var model = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(value);
        if (model?.StartsWith(NvidiaPciModelPrefix,
                StringComparison.OrdinalIgnoreCase) == true ||
            // NVIDIA's USB vendor ID already participates in the strict
            // PnP vendor parser and WUA ID selection. Require full VID+PID
            // grammar; a VID-only hint or private instance is NOT a device.
            model?.StartsWith(@"USB\VID_0955&PID_",
                StringComparison.OrdinalIgnoreCase) == true ||
            IsNvidiaPlatformId(value))
            return true;

        // Older generic HD Audio compatible IDs can omit DEV entirely.
        // Preserve that exact bus/vendor token, but reject invented
        // suffixes and malformed DEV values.
        return string.Equals(value, NvidiaAudioVendor,
            StringComparison.OrdinalIgnoreCase) ||
            model?.StartsWith(NvidiaAudioVendor + "&DEV_",
                StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsNvidiaPlatformId(string? value)
    {
        // Reject fabricated UID/suffix and malformed revision tokens.
        // Valid ACPI NVDA and DCH SWC model IDs survive, including REV.
        var model = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(value);
        return model?.StartsWith(@"ACPI\NVDA",
                   StringComparison.OrdinalIgnoreCase) == true ||
               model?.StartsWith(@"SWC\VEN_10DE&DEV_",
                   StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool ContainsNvidia(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(
            "NVIDIA",
            StringComparison.OrdinalIgnoreCase);
}
