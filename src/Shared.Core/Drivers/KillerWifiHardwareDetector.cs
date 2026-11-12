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
        // PNPClass may be absent before the Killer adapter has a vendor
        // driver. Recover its class only from real PCI class evidence.
        var pciNetwork = PciClassIdentity.HasNetworkClass(device, "8086");
        if (!string.IsNullOrWhiteSpace(type) &&
            !type.Equals("Net", StringComparison.OrdinalIgnoreCase) &&
            !pciNetwork)
            return false;
        if (string.IsNullOrWhiteSpace(type) &&
            !HasNetworkName(device.Name) && !pciNetwork)
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
        // The generic single-token PnP guard does not validate PCI field
        // names: "&FAKE_01" is syntactically a token but not an AX1675i
        // revision. Only the exact OEM SUBSYS, optionally followed by ONE
        // two-hex-digit REV field, identifies this Killer model.
        if (!PnpIdentifierFallback.IsStrictSingleId(id))
            return false;

        return KnownModels.Any(model =>
        {
            if (!id!.StartsWith(model, StringComparison.OrdinalIgnoreCase))
                return false;
            var suffix = id[model.Length..];
            return suffix.Length == 0 ||
                (suffix.Length == 7 &&
                 suffix.StartsWith("&REV_", StringComparison.OrdinalIgnoreCase) &&
                 Uri.IsHexDigit(suffix[5]) && Uri.IsHexDigit(suffix[6]));
        });
    }
}
