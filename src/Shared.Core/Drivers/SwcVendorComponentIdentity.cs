using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Read-only PnP identity for vendor DCH software/extension components.
/// A valid SWC bus+vendor+model token is NOT a vendor catalog result,
/// an available driver or proof that Windows will install anything.
/// </summary>
public static class SwcVendorComponentIdentity
{
    public static bool IsIntel(DriverInfo driver) =>
        HasVendorModel(driver, "8086");

    public static bool IsRealtek(DriverInfo driver) =>
        HasVendorModel(driver, "10EC");

    private static bool HasVendorModel(DriverInfo driver, string vendor)
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (!string.Equals(driver.DeviceClass, "SoftwareComponent",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(driver.DeviceClass, "Extension",
                StringComparison.OrdinalIgnoreCase))
            return false;

        var prefix = $@"SWC\VEN_{vendor}&DEV_";
        foreach (var raw in driver.HardwareIds.Concat(driver.CompatibleIds))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var id = raw.Trim();
            if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                id.Length < prefix.Length + 4)
                continue;

            var model = id.AsSpan(prefix.Length, 4);
            if (!model.ToString().All(Uri.IsHexDigit))
                continue;
            if (id.Length == prefix.Length + 4 ||
                id[prefix.Length + 4] == '&')
                return true;
        }
        return false;
    }
}
