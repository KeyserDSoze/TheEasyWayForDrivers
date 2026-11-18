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
    // Normalize only ASCII padding in the *comparison key* of a Windows
    // PnP instance. Keep the original DeviceId and Hardware IDs intact.
    internal static string NormalizeInstanceKey(string deviceId) =>
        deviceId.Trim(' ');

    public static IReadOnlyList<DriverInfo> Create(
        IEnumerable<DriverInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return devices
            .Where(device => device is not null &&
                !string.IsNullOrWhiteSpace(device.DeviceId))
            .GroupBy(device => NormalizeInstanceKey(device.DeviceId),
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
                     recognized[0] != PnpKnownVendorIdentity.Vendor.Conflicting) &&
                    !HasConflictingConcreteHardwareModels(group);
            })
            .Select(ChooseRepresentative)
            .ToArray();
    }

    /// <summary>
    /// For a SAME-INSTANCE duplicate, all strictly validated primary
    /// HardwareIds (including valid, unrecognized-vendor models) must name
    /// one coherent concrete device model, even
    /// when a single Windows row contains multiple primary IDs.
    /// A generic FIRST row cannot hide contradictory later NVIDIA GPU,
    /// Killer Wi-Fi or Realtek models: it may itself carry a specific
    /// CompatibleId that would otherwise create a false WUA match.
    ///
    /// CompatibleIds, driver titles and WMI provider names are not
    /// primary identity evidence; they never veto genuine HardwareIds.
    /// A public VEN+DEV and its ONE real OEM SUBSYS remain coherent.
    /// No ID is synthesized, and no driver becomes installable here.
    /// </summary>
    private static bool HasConflictingConcreteHardwareModels(
        IGrouping<string, DriverInfo> group)
    {
        string? expectedModel = null;
        var hasUnknownPrimaryModel = false;
        var subsystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The identity consensus is independent of provider row order.
        // Inspect every primary HardwareId, even when the first snapshot
        // is class-only, vendor-only or empty. Skip only unvalidated
        // or non-correlatable tokens; weak CompatibleIds do not attest
        // a different board. Vendor support is NOT an identity test.
        foreach (var device in group)
        {
            foreach (var id in device.HardwareIds)
            {
                // A strictly validated PCI/USB/audio model remains
                // primary evidence even if its manufacturer has no OEM
                // adapter. Ignoring an unknown but real VEN/VID/MAN here
                // could falsely correlate an unrelated NVIDIA/Intel ID.
                if (!IsSpecificId(id))
                    continue;

                var model = MicrosoftUpdateCatalogSearch
                    .TryGetPublicModelKey(id);
                if (model is null)
                    continue;

                // A public model with an unsupported vendor is still
                // authoritative primary identity, even if another WMI row
                // has only a known-vendor CompatibleId. That alias cannot
                // rebrand this actual device into an NVIDIA/Intel offer.
                if (PnpKnownVendorIdentity.FromId(id) is null)
                    hasUnknownPrimaryModel = true;

                if (expectedModel is null)
                    expectedModel = model;
                else if (!string.Equals(expectedModel, model,
                        StringComparison.OrdinalIgnoreCase))
                    return true;

                var subsystem = SubsystemCode(id);
                if (subsystem is not null &&
                    subsystems.Add(subsystem) && subsystems.Count > 1)
                    return true;
            }
        }

        // #456: a generic WMI row with a specific known CompatibleId can
        // otherwise survive beside a different SAME-instance row with one
        // unknown-vendor primary model. The selected generic row would
        // falsely correlate WUA/INF by its weaker compatible alias.
        // Only strictly validated primary public models can trigger this
        // veto; a malformed hint, ROOT class or foreign compatible alias
        // must not suppress a genuine recognized primary HardwareId.
        return hasUnknownPrimaryModel && group
            .SelectMany(device => device.CompatibleIds)
            .Any(id => IsSpecificId(id) &&
                PnpKnownVendorIdentity.FromId(id) is
                    { } known &&
                known != PnpKnownVendorIdentity.Vendor.Conflicting);
    }

    private static string? SubsystemCode(string id)
    {
        // Only called on strictly validated correlated PnP IDs. Ignore
        // REV qualifiers: the vendor's eight-digit OEM SUBSYS, not
        // a different presentation of the same PnP revision, is compared.
        var index = id.IndexOf("&SUBSYS_", StringComparison.OrdinalIgnoreCase);
        return index < 0 || id.Length < index + 16
            ? null : id.Substring(index + 8, 8);
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

        // A generic first WMI row may be followed by both a public
        // PCI VEN+DEV row and a more precise SAME-instance
        // VEN+DEV+SUBSYS hardware row. Those are not textually identical,
        // but can corroborate one model without merging any ID strings.
        if (TryPreferGenericFirstQualifiedPciModel(
                group, first, out var corroboratedPci))
            return corroboratedPci;

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
    /// Only for a GENERIC first row: later snapshots of the same DeviceId
    /// can independently declare the same PCI VEN+DEV, with one of them
    /// also declaring a concrete SUBSYS Hardware ID. Preserve the genuine
    /// OEM-specific record when there is ONE unambiguous qualified ID.
    /// A base-only record corroborates the model; it does not have to
    /// repeat the full SUBSYS text. No different model/vendor, ambiguous
    /// SUBSYS/REV, unvalidated hint or compatible-only SUBSYS may promote.
    /// This is read-only PnP evidence, not an installation verdict.
    /// </summary>
    private static bool TryPreferGenericFirstQualifiedPciModel(
        IGrouping<string, DriverInfo> group,
        DriverInfo first, out DriverInfo selected)
    {
        selected = first;
        var later = group.Skip(1).ToArray();
        var publicModel = later
            .SelectMany(device => device.HardwareIds)
            .Where(IsSpecificId)
            .Select(MicrosoftUpdateCatalogSearch.TryGetPublicModelKey)
            .FirstOrDefault(model => model is not null &&
                model.StartsWith(@"PCI\VEN_", StringComparison.Ordinal));
        if (publicModel is null ||
            PnpKnownVendorIdentity.FromId(publicModel) is not
                { } knownVendor ||
            knownVendor == PnpKnownVendorIdentity.Vendor.Conflicting)
            return false;

        var sawBareModel = false;
        var qualified = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        DriverInfo? qualifiedOwner = null;
        foreach (var device in group)
        {
            foreach (var id in device.HardwareIds)
            {
                if (!PnpIdentifierFallback.IsStrictSingleId(id))
                    return false;
                if (IsSpecificId(id))
                {
                    if (!string.Equals(
                            MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id),
                            publicModel, StringComparison.OrdinalIgnoreCase) ||
                        PnpKnownVendorIdentity.FromId(id) != knownVendor)
                        return false;

                    if (string.Equals(id, publicModel,
                            StringComparison.OrdinalIgnoreCase))
                        sawBareModel = true;
                    if (id.StartsWith(publicModel + "&SUBSYS_",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        qualified.Add(id);
                        qualifiedOwner ??= device;
                    }
                }
                else if (!IsGenericHintForModel(id, publicModel))
                    return false;
            }

            foreach (var id in device.CompatibleIds)
            {
                if (!PnpIdentifierFallback.IsStrictSingleId(id))
                    return false;
                if (IsSpecificId(id))
                {
                    if (!string.Equals(
                            MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id),
                            publicModel, StringComparison.OrdinalIgnoreCase) ||
                        PnpKnownVendorIdentity.FromId(id) != knownVendor)
                        return false;
                }
                else if (!IsGenericHintForModel(id, publicModel))
                    return false;
            }
        }

        // Keep existing single-complete-model recovery unchanged. Only
        // add the missing case of a corroborating BARE Hardware ID plus
        // exactly one real, more specific OEM Hardware ID.
        if (!sawBareModel || qualified.Count != 1 ||
            qualifiedOwner is null)
            return false;

        selected = qualifiedOwner;
        return true;
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
