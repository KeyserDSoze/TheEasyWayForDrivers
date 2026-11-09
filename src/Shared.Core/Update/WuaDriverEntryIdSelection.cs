using TheEasyWayForDrivers.Core.Drivers;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Selects a bounded, representative subset of already VALIDATED WUA
/// IWindowsDriverUpdateEntry Hardware IDs. COM order alone can bury an
/// applicable NVIDIA/Killer/Realtek ID after the 1024-ID IPC ceiling.
/// No hardware/name inference, new update GUID or installation permission.
/// </summary>
public static class WuaDriverEntryIdSelection
{
    // Keep the overwhelming majority of the old first-seen behavior.
    // Only when the WUA collection exceeds the transport cap do we
    // reserve space for already-declared late major-vendor IDs.
    public const int ReservedVendorSlots = 64;

    public sealed record Result(IReadOnlyList<string> HardwareIds, int DeferredIds);

    public static Result Select(IReadOnlyList<string> validatedDistinctIds) =>
        SelectCore(validatedDistinctIds,
            WuaDriverEntryLimits.MaximumEntriesToInspect);

    /// <summary>
    /// Two fully enumerated WUA passes may each contribute up to 2048
    /// IDs plus the other pass's primary ID. Apply the SAME vendor-fair
    /// transport selection AFTER merging, not only inside each pass.
    /// The public per-pass limit remains 2048 and the IPC cap 1024.
    /// </summary>
    public static Result SelectMerged(IReadOnlyList<string> validatedDistinctIds) =>
        SelectCore(validatedDistinctIds,
            checked(2 * WuaDriverEntryLimits.MaximumEntriesToInspect + 1));

    private static Result SelectCore(IReadOnlyList<string> validatedDistinctIds,
        int maximumInputCount)
    {
        ArgumentNullException.ThrowIfNull(validatedDistinctIds);
        if (validatedDistinctIds.Count > maximumInputCount)
            throw new InvalidDataException("Troppe entry WUA per la selezione hardware.");
        if (validatedDistinctIds.Count <= WuaDriverEntryLimits.MaximumAdditionalIds)
            return new(validatedDistinctIds.ToArray(), 0);

        var baseline = WuaDriverEntryLimits.MaximumAdditionalIds -
            ReservedVendorSlots;
        var selected = validatedDistinctIds.Take(baseline).ToList();
        var seen = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // The eighth bucket preserves late SoundWire SDCA codec models
        // without guessing which chip vendor owns a MAN identifier.
        var buckets = new List<string>[8];
        for (var group = 0; group < buckets.Length; group++)
            buckets[group] = [];

        // All raw strings are validated and de-duplicated by the WUA COM
        // reader before this method is called. Use fixed bus+vendor tokens
        // from the actual Hardware ID, NEVER update title/model text.
        foreach (var id in validatedDistinctIds.Skip(baseline))
        {
            var group = GetVendorGroup(id);
            if (group >= 0 && !seen.Contains(id))
                buckets[group].Add(id);
        }

        // One ID per vendor in each round, so 60 late NVIDIA entries cannot
        // hide a single Killer/Realtek entry at the very end of the pass.
        var next = new int[buckets.Length];
        while (selected.Count < WuaDriverEntryLimits.MaximumAdditionalIds)
        {
            var advanced = false;
            for (var group = 0;
                 group < buckets.Length &&
                 selected.Count < WuaDriverEntryLimits.MaximumAdditionalIds;
                 group++)
            {
                var bucket = buckets[group];
                if (next[group] >= bucket.Count)
                    continue;
                var id = bucket[next[group]++];
                if (seen.Add(id))
                {
                    selected.Add(id);
                    advanced = true;
                }
            }
            if (!advanced)
                break;
        }

        // If there are fewer than 64 late vendor IDs, do not waste slots:
        // restore remaining WUA IDs in original order, bounded and distinct.
        foreach (var id in validatedDistinctIds.Skip(baseline))
        {
            if (selected.Count == WuaDriverEntryLimits.MaximumAdditionalIds)
                break;
            if (seen.Add(id))
                selected.Add(id);
        }

        return new(selected, validatedDistinctIds.Count - selected.Count);
    }

    // NVIDIA / Intel-Killer / Realtek / AMD / Qualcomm-Atheros / Broadcom /
    // MediaTek, plus a vendor-neutral SoundWire SDCA codec group. Only
    // strict public model IDs (including ACPI and DCH SWC platform
    // components) receive reserved transport slots. Exact ID
    // equality with the Windows PnP inventory is still required later;
    // classification never proves applicability or installability.
    private static int GetVendorGroup(string id)
    {
        // The WUA entry reader validates strings, not a full PnP model.
        // A prefix such as PCI\\VEN_10DE&FAKE_... must NOT consume one of
        // the 64 vendor slots and hide a genuine late NVIDIA/Killer ID.
        // Reuse the strict read-only public-model parser; this does not
        // turn a WUA entry into a compatible or installable driver.
        var model = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id);
        if (model is null)
            return -1;

        // Generic SOUNDWIRE\AGGREGATOR / AGGREGATEDSPEAKER must never
        // consume priority slots. Retain only specifically declared codec
        // IDs, where the strict parser confirmed SDCA, MAN and FUNC.
        if (model.StartsWith(@"SOUNDWIRE\SDCA_", StringComparison.Ordinal) &&
            DriverUpdateMatcher.IsCorrelatableHardwareId(id))
            return 7;

        if (BusVendor(id, @"PCI\VEN_10DE") ||
            BusVendor(id, @"HDAUDIO\FUNC_01&VEN_10DE") ||
            BusVendor(id, @"USB\VID_0955") ||
            BusVendor(id, @"SWC\VEN_10DE") ||
            NvidiaAcpiDeviceId(id))
            return 0;

        if (BusVendor(id, @"PCI\VEN_8086") ||
            BusVendor(id, @"PCI\VEN_1A56") ||
            BusVendor(id, @"USB\VID_8087") ||
            BusVendor(id, @"HDAUDIO\FUNC_01&VEN_8086") ||
            BusVendor(id, @"INTELAUDIO\FUNC_01&VEN_8086") ||
            BusVendor(id, @"SWC\VEN_8086"))
            return 1;

        if (BusVendor(id, @"PCI\VEN_10EC") ||
            BusVendor(id, @"HDAUDIO\FUNC_01&VEN_10EC") ||
            BusVendor(id, @"INTELAUDIO\FUNC_01&VEN_10EC") ||
            BusVendor(id, @"USB\VID_0BDA") ||
            BusVendor(id, @"SWC\VEN_10EC"))
            return 2;

        if (BusVendor(id, @"PCI\VEN_1002") ||
            BusVendor(id, @"PCI\VEN_1022") ||
            BusVendor(id, @"HDAUDIO\FUNC_01&VEN_1002"))
            return 3;

        if (BusVendor(id, @"PCI\VEN_17CB") ||
            BusVendor(id, @"PCI\VEN_168C") ||
            BusVendor(id, @"USB\VID_0CF3") ||
            BusVendor(id, @"USB\VID_05C6"))
            return 4;

        if (BusVendor(id, @"PCI\VEN_14E4") ||
            BusVendor(id, @"USB\VID_0A5C"))
            return 5;

        if (BusVendor(id, @"PCI\VEN_14C3") ||
            BusVendor(id, @"USB\VID_0E8D"))
            return 6;

        return -1;
    }

    // A generic ACPI name is not evidence of NVIDIA. Recognize only
    // the specific NVDA vendor HIDs used for platform-controller PnP
    // identity: ACPI\\NVDA followed by exactly four hex digits, then
    // the end or an '&' revision suffix. Never use update title strings.
    private static bool NvidiaAcpiDeviceId(string id)
    {
        const string prefix = @"ACPI\NVDA";
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            id.Length < prefix.Length + 4)
            return false;

        for (var i = prefix.Length; i < prefix.Length + 4; i++)
        {
            if (!Uri.IsHexDigit(id[i]))
                return false;
        }

        var end = prefix.Length + 4;
        return id.Length == end || id[end] == '&';
    }

    private static bool BusVendor(string id, string prefix) =>
        id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        (id.Length == prefix.Length || id[prefix.Length] == '&');
}
