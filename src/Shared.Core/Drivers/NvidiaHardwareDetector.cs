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

        var id = value.Trim();
        if (id.StartsWith(NvidiaPciMarker + "&", StringComparison.OrdinalIgnoreCase))
            return true;

        // DCH SoftwareComponent nodes and notebook ACPI controllers
        // frequently have generic Microsoft names. Require an actual
        // NVIDIA bus+model token rather than accepting arbitrary suffixes.
        if (IsNvidiaPlatformId(id))
            return true;

        if (!id.StartsWith(@"HDAUDIO\", StringComparison.OrdinalIgnoreCase))
            return false;

        return id[@"HDAUDIO\".Length..]
            .Split('&')
            .Any(part => string.Equals(part, "VEN_10DE", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNvidiaPlatformId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var id = value.Trim();
        return HasHexModelId(id, @"ACPI\NVDA") ||
            HasHexModelId(id, @"SWC\VEN_10DE&DEV_");
    }

    private static bool HasHexModelId(string id, string prefix)
    {
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            id.Length < prefix.Length + 4)
            return false;

        for (var i = prefix.Length; i < prefix.Length + 4; i++)
        {
            if (!Uri.IsHexDigit(id[i]))
                return false;
        }

        var end = prefix.Length + 4;
        return id.Length == end || id[end] == '&';
    }

    private static bool ContainsNvidia(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(
            "NVIDIA",
            StringComparison.OrdinalIgnoreCase);
}
