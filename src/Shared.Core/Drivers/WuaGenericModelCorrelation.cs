using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Extra, REVIEW-ONLY WUA correlation for devices whose Win32_PnPEntity
/// hardware IDs contain a SUBSYS/revision but CompatibleID is unavailable.
/// An official WUA entry may declare only the public vendor/model token.
/// THIS IS NOT a Windows-selected driver, verified OEM compatibility, or
/// authority to enable one-click installation.
/// </summary>
public static class WuaGenericModelCorrelation
{
    public const int MaximumReviewHintsPerDevice = 16;

    private static readonly HashSet<string> OfficialSources =
        WindowsDriverSearchPlan.Create(DriverSearchMode.Comprehensive, true)
            .Select(pass => pass.Name)
            .Append("Windows Update")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsWeakModelOnlyMatch(
        DriverInfo device, DriverUpdateInfo update)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(update);

        if (!Guid.TryParse(update.Id, out _) ||
            !OfficialSources.Contains(update.SearchSource) ||
            QuickUpdatePolicy.IsFirmwareOrBios(update.Title, update.DriverClass) ||
            DriverUpdateMatcher.MatchStrength(device, update) != 0)
            return false;

        // Only validated *primary* Windows Hardware IDs can generate a
        // review key. Two narrowly distinct, NON-installable cases exist:
        // 1) the WUA declaration has only the generic public VEN+DEV,
        // 2) WUA declares EXACTLY the same OEM SUBSYS, with only terminal
        //    REV_XX omitted. Never strip SUBSYS or infer from title/provider.
        var genericModels = PublicReviewModelKeys(device);
        var sameSubsys = InfModelEvidence.RevisionlessSubsysReviewKeys(device);
        var sameProductVariant = SpecificProductVariantReviewKeys(device);
        if (genericModels.Count == 0 && sameSubsys.Count == 0 &&
            sameProductVariant.Count == 0)
            return false;

        var declared = new[] { update.HardwareId }
            .Concat((update.AdditionalHardwareIds ?? [])
                .Take(WuaDriverEntryLimits.MaximumAdditionalIds));

        return declared.Any(id =>
            id is not null &&
            // The declared WUA string must be EXACTLY a generated review
            // key, never a longer suffix, private instance or prefix match.
            (sameSubsys.Contains(id) ||
             (sameProductVariant.Contains(id) &&
              DriverUpdateMatcher.IsCorrelatableHardwareId(id)) ||
             (genericModels.Contains(id) &&
              string.Equals(
                  MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id),
                  id.ToUpperInvariant(), StringComparison.Ordinal))));
    }

    /// <summary>
    /// Combined WUA review keys, NOT Windows Compatible IDs. The additional
    /// exact-SUBSYS-without-REV keys share the same strict parser used by
    /// the local OEM INF comparison; all consumers of the indexed WUA
    /// association must use these keys rather than just generic VEN+DEV.
    /// </summary>
    public static IReadOnlySet<string> ReviewModelKeys(DriverInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var keys = PublicReviewModelKeys(device)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(InfModelEvidence.RevisionlessSubsysReviewKeys(device));
        keys.UnionWith(SpecificProductVariantReviewKeys(device));
        return keys;
    }

    /// <summary>
    /// Read-only HID/USBSTOR review keys for a WUA entry that declares only
    /// the SAME complete product model without the Windows-specific HID
    /// attributes or USB storage revision. The original HardwareID must be
    /// strictly validated first; the shortened model must remain a specific
    /// PnP product, never a generic class, manufacturer or instance path.
    /// These are NOT CompatibleIDs and cannot authorize installation.
    /// </summary>
    private static IReadOnlySet<string> SpecificProductVariantReviewKeys(
        DriverInfo device)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in device.HardwareIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (id.StartsWith(@"HID\VID_", StringComparison.OrdinalIgnoreCase) &&
                HidPnpMatchPolicy.IsSpecificHardwareId(id))
            {
                const int modelLength = 8 + 4 + 5 + 4; // HID\\VID_XXXX&PID_YYYY
                if (id.Length > modelLength && id[modelLength] == '&')
                {
                    var model = id[..modelLength];
                    if (HidPnpMatchPolicy.IsSpecificHardwareId(model))
                        keys.Add(model);
                }
            }

            if (id.StartsWith(@"USBSTOR\", StringComparison.OrdinalIgnoreCase) &&
                UsbStorPnpMatchPolicy.IsSpecificHardwareId(id))
            {
                // Only Windows' structured &Ven_...&Prod_...[&Rev_...]
                // spelling can be shortened. Padded vendor/product strings
                // have no removable separator and must remain exact.
                var revisionAt = id.LastIndexOf("&Rev_",
                    StringComparison.OrdinalIgnoreCase);
                if (revisionAt > 0)
                {
                    var product = id[..revisionAt];
                    if (UsbStorPnpMatchPolicy.IsSpecificHardwareId(product))
                        keys.Add(product);
                }
            }
        }

        return keys;
    }

    /// <summary>
    /// Build only public, review-only model keys from device Hardware IDs.
    /// NVIDIA NVDA ACPI controllers may expose an explicit revision while
    /// WUA declares the public four-hex HID: this is REVIEW-ONLY evidence.
    /// Shared with the indexed WUA-PnP audit so it cannot invent matches
    /// that the per-device weak matcher would reject.
    /// </summary>
    public static IReadOnlySet<string> PublicReviewModelKeys(DriverInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return device.HardwareIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => new
            {
                Raw = id!,
                Key = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id)
            })
            .Where(model => model.Key is not null &&
                !model.Raw.Equals(model.Key, StringComparison.OrdinalIgnoreCase) &&
                IsSpecificModelBus(model.Key))
            .Select(model => model.Key!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSpecificModelBus(string key) =>
        key.StartsWith(@"PCI\", StringComparison.Ordinal) ||
        key.StartsWith(@"USB\", StringComparison.Ordinal) ||
        key.StartsWith(@"HDAUDIO\", StringComparison.Ordinal) ||
        key.StartsWith(@"INTELAUDIO\", StringComparison.Ordinal) ||
        key.StartsWith(@"SWC\", StringComparison.Ordinal) ||
        // Restrict ACPI weak correlation to the already recognized NVIDIA
        // vendor HID, not arbitrary ACPI/PNP model families.
        (key.StartsWith(@"ACPI\NVDA", StringComparison.Ordinal) &&
         key.Length == @"ACPI\NVDA".Length + 4 &&
         key[@"ACPI\NVDA".Length..].All(Uri.IsHexDigit));
}
