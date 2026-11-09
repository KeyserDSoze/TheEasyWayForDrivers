using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Recognizes the documented Killer Wi-Fi 6E AX1675i 211NGW subsystem,
/// not all Intel 51F1 radios. This is PnP evidence, not driver availability.
/// </summary>
public static class KillerWifiHardwareDetector
{
    private static readonly string[] KnownModels =
    [
        @"PCI\VEN_8086&DEV_51F0&SUBSYS_16721A56",
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56"
    ];

    public static bool IsKnownAx1675i(DriverInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var type = device.DeviceClass;
        if (!string.IsNullOrWhiteSpace(type) &&
            !type.Equals("Net", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(type) &&
            !HasNetworkName(device.Name))
            return false;
        return device.HardwareIds.Concat(device.CompatibleIds)
            .Any(IsExactModel);
    }

    private static bool HasNetworkName(string? name) =>
        name is not null &&
        (name.Contains("Network", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("WiFi", StringComparison.OrdinalIgnoreCase));

    private static bool IsExactModel(string? id)
    {
        // This guard rejects instance paths, malformed IDs and whitespace;
        // a prefix such as SUBSYS_16721A560 is not a different suffix.
        if (!PnpIdentifierFallback.IsStrictSingleId(id))
            return false;
        return KnownModels.Any(model =>
            id!.StartsWith(model, StringComparison.OrdinalIgnoreCase) &&
            (id.Length == model.Length || id[model.Length] == '&'));
    }
}
