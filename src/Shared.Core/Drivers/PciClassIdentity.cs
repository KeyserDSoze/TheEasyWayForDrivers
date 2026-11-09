using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Read-only, conservative recovery of PCI display/network class when WMI
/// PNPClass is absent or generic. Requires an explicit VEN+DEV PnP model
/// AND a separate, correctly delimited PCI CC class code on the SAME device.
/// Class-only IDs, names, driver providers and instance paths cannot create
/// an OEM vendor attribution or an installable update.
/// </summary>
public static class PciClassIdentity
{
    public static bool HasDisplayClass(DriverInfo device, string vendor) =>
        HasClass(device, vendor, "0300", "0302", "0380");

    public static bool HasNetworkClass(DriverInfo device, string vendor) =>
        HasClass(device, vendor, "0200", "0280");

    private static bool HasClass(DriverInfo device, string vendor,
        params string[] subclasses)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (vendor.Length != 4 || !vendor.All(Uri.IsHexDigit))
            return false;

        // An explicit Windows class must not be contradicted by a lower
        // priority class-code hint from a malformed or stale compatible ID.
        var pnpClass = device.DeviceClass?.Trim();
        if (!string.IsNullOrEmpty(pnpClass) &&
            !pnpClass.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
            !pnpClass.Equals("System", StringComparison.OrdinalIgnoreCase) &&
            !pnpClass.Equals("Other", StringComparison.OrdinalIgnoreCase))
            return false;

        var ids = device.HardwareIds.Concat(device.CompatibleIds).ToArray();
        var prefix = $@"PCI\VEN_{vendor}&DEV_";
        var hasVendorModel = ids.Any(id =>
            PnpIdentifierFallback.IsStrictSingleId(id) &&
            id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            id.Length >= prefix.Length + 4 &&
            id.AsSpan(prefix.Length, 4).ToString().All(Uri.IsHexDigit) &&
            (id.Length == prefix.Length + 4 ||
             id[prefix.Length + 4] == '&'));

        if (!hasVendorModel)
            return false;

        foreach (var id in ids)
        {
            if (!PnpIdentifierFallback.IsStrictSingleId(id) ||
                !id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase))
                continue;

            var tokens = id[@"PCI\".Length..].Split('&');
            // The generic PCI\CC_XXXX token is shared with VEN+DEV via
            // this same device's PnP object. If a token names a vendor,
            // require that vendor to be the same exact four hex digits.
            if (tokens[0].StartsWith("VEN_", StringComparison.OrdinalIgnoreCase) &&
                !tokens[0].Equals("VEN_" + vendor,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var token in tokens)
            {
                if (!token.StartsWith("CC_", StringComparison.OrdinalIgnoreCase))
                    continue;
                var code = token[3..];
                if (code.Length is not (4 or 6) || !code.All(Uri.IsHexDigit))
                    continue;
                if (subclasses.Contains(code[..4], StringComparer.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }
}
