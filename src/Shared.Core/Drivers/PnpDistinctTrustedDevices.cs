using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// One read-only PnP instance cannot simultaneously be an NVIDIA GPU and
/// an Intel/Realtek/Qualcomm device. A WMI and SignedDriver snapshot may
/// contain repeated rows for the same DeviceId. Discard contradictory
/// vendors, retain an already specific first row, and only recover a later
/// complete model where every same-instance generic hint is consistent
/// with that model. Never combine IDs from independent rows.
/// This is not an update-eligibility or install gate.
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
            .Select(ChooseRepresentative)
            .ToArray();
    }

    // A duplicate generic first WMI row can suppress a complete, valid
    // same-instance SignedDriver model (NVIDIA GPU/HD Audio, Intel Killer,
    // SWC, USB, HID, SoundWire). Promote the second row only when no
    // primary or compatible MODEL exists on the first row and ALL other
    // rows agree with the candidate's exact model/bus/vendor constraints.
    // Do not merge hardware and compatible IDs from different snapshots.
    private static DriverInfo ChooseRepresentative(
        IGrouping<string, DriverInfo> group)
    {
        var first = group.First();
        if (first.HardwareIds.Concat(first.CompatibleIds).Any(IsSpecificId))
            return first;

        var fullRows = group.Skip(1)
            .Select(device => (Device: device, Specific: device.HardwareIds
                .Where(IsSpecificId).ToArray()))
            .Where(row => row.Specific.Length > 0)
            .ToArray();
        if (fullRows.Length == 0)
            return first;

        var exact = fullRows[0].Specific[0];
        var vendor = PnpKnownVendorIdentity.FromId(exact);
        if (vendor is null or PnpKnownVendorIdentity.Vendor.Conflicting)
            return first;

        // A duplicated DeviceId with two different full models is not a
        // reason to guess which one is this PC's hardware. Nor is a known
        // compatible model proof if another vendor is present.
        if (fullRows.Any(row =>
            !row.Specific.Contains(exact, StringComparer.OrdinalIgnoreCase) ||
            row.Specific.Any(id => PnpKnownVendorIdentity.FromId(id) != vendor) ||
            row.Device.CompatibleIds.Any(id =>
                IsSpecificId(id) &&
                PnpKnownVendorIdentity.FromId(id) != vendor)))
            return first;

        foreach (var row in group)
        {
            if (fullRows.Any(full => ReferenceEquals(full.Device, row)))
                continue;

            // A generic row that has *no* IDs makes no claims. Any
            // vendor-only/class/function hint must instead be validated
            // against the later complete model, not silently ignored.
            if (row.HardwareIds.Concat(row.CompatibleIds).Any(id =>
                !IsGenericHintForModel(id, exact)))
                return first;
        }

        return fullRows[0].Device;
    }

    private static bool IsSpecificId(string? id) =>
        PnpIdentifierFallback.IsStrictSingleId(id) &&
        DriverUpdateMatcher.IsCorrelatableHardwareId(id);

    private static bool IsGenericHintForModel(string? id, string model)
    {
        if (!PnpIdentifierFallback.IsStrictSingleId(id) ||
            IsSpecificId(id))
            return false;

        const string pciVendor = @"PCI\VEN_";
        if (id!.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase))
        {
            if (!model.StartsWith(pciVendor, StringComparison.OrdinalIgnoreCase) ||
                !PnpSetupApiIdentity.NeedsPciHardwareModelRecovery([id]))
                return false;

            if (!id.StartsWith(pciVendor, StringComparison.OrdinalIgnoreCase))
                return true; // PCI class-only; it does not claim a vendor.

            var code = id.Substring(pciVendor.Length, 4);
            return model.StartsWith(pciVendor + code + "&DEV_",
                StringComparison.OrdinalIgnoreCase);
        }

        return PnpNonPciModelRecovery.TryGetScope([id], out var scope) &&
            PnpNonPciModelRecovery.IsMatchingNativeModel(model, scope);
    }
}
