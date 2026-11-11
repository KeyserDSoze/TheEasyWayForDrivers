using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Count distinct physical PnP instances for OEM companion availability,
/// not driver updates. Shared by NVIDIA/Intel/AMD service providers so the
/// OEM panel cannot contradict the strict hardware vendor shown on Home.
/// Does not query any catalog, identify a newer version or install a driver.
/// </summary>
public static class OemCompanionDeviceCoverage
{
    public static int Count(
        IEnumerable<DriverInfo> drivers,
        PnpKnownVendorIdentity.Vendor target,
        Func<DriverInfo, bool> legacyDetector)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        ArgumentNullException.ThrowIfNull(legacyDetector);
        if (target is not (PnpKnownVendorIdentity.Vendor.Nvidia or
                PnpKnownVendorIdentity.Vendor.Intel or
                PnpKnownVendorIdentity.Vendor.Amd))
            throw new ArgumentOutOfRangeException(nameof(target));

        var pciVendors = target switch
        {
            PnpKnownVendorIdentity.Vendor.Nvidia => new[] { "10DE" },
            PnpKnownVendorIdentity.Vendor.Intel => ["8086", "1A56"],
            _ => ["1002", "1022"]
        };

        // A WMI/SignedDriver inventory may contain repeated rows for
        // the same PnP node. Do not count those as multiple devices.
        // Empty IDs carry no stable device identity. If two records for
        // the SAME node carry mutually inconsistent recognized vendors,
        // fail closed instead of selecting whichever appeared first.
        return drivers
            .Where(d => d is not null &&
                !string.IsNullOrWhiteSpace(d.DeviceId))
            .GroupBy(d => d.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Count(group =>
            {
                var known = group
                    .Select(PnpKnownVendorIdentity.FromDriver)
                    .Where(v => v is not null)
                    .Distinct()
                    .ToArray();

                if (known.Contains(PnpKnownVendorIdentity.Vendor.Conflicting) ||
                    known.Length > 1)
                    return false;

                if (known.Length == 1)
                    return known[0] == target;

                // Preserve the existing vendor-text fallback only where
                // validated public PnP models cannot identify a vendor.
                // An UNKNOWN but concrete foreign PCI VEN+DEV still
                // vetoes stale provider text (no ambiguous OEM handoff).
                return group.Any(legacyDetector) &&
                    group.All(device =>
                        !VendorSourceDiscovery.HasForeignPciVendor(
                            device, pciVendors));
            });
    }
}
