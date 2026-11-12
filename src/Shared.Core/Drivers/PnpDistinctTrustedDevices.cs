using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// One read-only PnP instance cannot simultaneously be an NVIDIA GPU and
/// an Intel/Realtek/Qualcomm device. A WMI and SignedDriver snapshot may
/// contain repeated rows for the same DeviceId. Retain first-row semantics
/// for consistent/unknown vendors, but discard the entire contradictory
/// group before any Home, vendor-coverage, WUA diagnostic or support-link
/// classification. This is not an update-eligibility or install gate.
/// </summary>
public static class PnpDistinctTrustedDevices
{
    public static IReadOnlyList<DriverInfo> Create(
        IEnumerable<DriverInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return devices
            .Where(device => device is not null &&
                !string.IsNullOrWhiteSpace(device.DeviceId))
            .GroupBy(device => device.DeviceId,
                StringComparer.OrdinalIgnoreCase)
            .Where(group =>
            {
                // Mirror OEM companion's existing fail-closed policy.
                // Do not select whichever recognized vendor happened
                // to appear first in a duplicated Windows inventory.
                var recognized = group
                    .Select(PnpKnownVendorIdentity.FromDriver)
                    .Where(vendor => vendor is not null)
                    .Distinct()
                    .Take(2)
                    .ToArray();
                return recognized.Length < 2 &&
                    (recognized.Length == 0 ||
                     recognized[0] != PnpKnownVendorIdentity.Vendor.Conflicting);
            })
            .Select(group => group.First())
            .ToArray();
    }
}
