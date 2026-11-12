using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// One read-only PnP instance cannot simultaneously be an NVIDIA GPU and
/// an Intel/Realtek/Qualcomm device. A WMI and SignedDriver snapshot may
/// contain repeated rows for the same DeviceId. Discard contradictory
/// vendors; preserve a specific first row except when another SAME-instance
/// row contains an independently verified, more precise PCI SUBSYS variant
/// of its exact VEN+DEV model. Generic-first recovery remains separately
/// constrained. Never combine IDs from independent rows.
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
        if (TryPreferQualifiedPciModel(group, first, out var qualifiedPci))
            return qualifiedPci;

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

    /// <summary>
    /// A first WMI row may contain only the public PCI VEN+DEV model while
    /// a second row of the SAME PnP instance contains the OEM-specific
    /// VEN+DEV+SUBSYS Hardware ID. Prefer that second complete row for
    /// read-only WUA/INF correlation only if every row (including compatible
    /// aliases) names exactly the same public model and all qualified
    /// SUBSYS identities are identical. Any uncertainty keeps the original
    /// bare model; we NEVER synthesize or merge IDs from two rows.
    /// </summary>
    private static bool TryPreferQualifiedPciModel(
        IGrouping<string, DriverInfo> group,
        DriverInfo first, out DriverInfo selected)
    {
        selected = first;
        if (first.HardwareIds.Count != 1)
            return false;

        var bare = first.HardwareIds[0];
        var publicModel = MicrosoftUpdateCatalogSearch
            .TryGetPublicModelKey(bare);
        if (publicModel is null ||
            !publicModel.StartsWith(@"PCI\VEN_", StringComparison.Ordinal) ||
            !string.Equals(bare, publicModel,
                StringComparison.OrdinalIgnoreCase) ||
            PnpKnownVendorIdentity.FromId(bare) is null)
            return false;

        var qualified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in group)
        {
            foreach (var id in device.HardwareIds.Concat(device.CompatibleIds))
            {
                if (!PnpIdentifierFallback.IsStrictSingleId(id))
                    return false;

                var model = MicrosoftUpdateCatalogSearch
                    .TryGetPublicModelKey(id);
                if (string.Equals(model, publicModel,
                        StringComparison.OrdinalIgnoreCase))
                {
                    // A SUBSYS-bearing ID is an actual Hardware ID in
                    // some row, not a stripped WUA Catalog query. Preserve
                    // its full original bytes for exact local matching.
                    if (id.StartsWith(publicModel + "&SUBSYS_",
                            StringComparison.OrdinalIgnoreCase))
                        qualified.Add(id);
                    continue;
                }

                if (!IsGenericHintForModel(id, publicModel))
                    return false;
            }
        }

        if (qualified.Count != 1)
            return false; // none, or two different OEM subsystem/revisions

        var originalQualified = qualified.Single();
        var candidate = group.Skip(1).FirstOrDefault(device =>
            device.HardwareIds.Contains(originalQualified,
                StringComparer.OrdinalIgnoreCase));
        if (candidate is null)
            return false; // CompatibleID-only evidence cannot promote

        selected = candidate;
        return true;
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
