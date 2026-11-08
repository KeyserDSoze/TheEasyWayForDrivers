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

    public static Result Select(IReadOnlyList<string> validatedDistinctIds)
    {
        ArgumentNullException.ThrowIfNull(validatedDistinctIds);
        if (validatedDistinctIds.Count > WuaDriverEntryLimits.MaximumEntriesToInspect)
            throw new InvalidDataException("Troppe entry WUA per la selezione hardware.");
        if (validatedDistinctIds.Count <= WuaDriverEntryLimits.MaximumAdditionalIds)
            return new(validatedDistinctIds.ToArray(), 0);

        var baseline = WuaDriverEntryLimits.MaximumAdditionalIds -
            ReservedVendorSlots;
        var selected = validatedDistinctIds.Take(baseline).ToList();
        var seen = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var buckets = new List<string>[7];
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
    // MediaTek. These public PCI/USB/HDAUDIO/INTELAUDIO bus IDs cannot be
    // confused with device instance paths or filenames. An isolated PCI
    // vendor token is not enough to prove a compatible driver.
    private static int GetVendorGroup(string id)
    {
        if (BusVendor(id, @"PCI\VEN_10DE") ||
            BusVendor(id, @"HDAUDIO\FUNC_01&VEN_10DE") ||
            BusVendor(id, @"USB\VID_0955"))
            return 0;

        if (BusVendor(id, @"PCI\VEN_8086") ||
            BusVendor(id, @"PCI\VEN_1A56") ||
            BusVendor(id, @"USB\VID_8087"))
            return 1;

        if (BusVendor(id, @"PCI\VEN_10EC") ||
            BusVendor(id, @"HDAUDIO\FUNC_01&VEN_10EC") ||
            BusVendor(id, @"INTELAUDIO\FUNC_01&VEN_10EC") ||
            BusVendor(id, @"USB\VID_0BDA"))
            return 2;

        if (BusVendor(id, @"PCI\VEN_1002") ||
            BusVendor(id, @"PCI\VEN_1022") ||
            BusVendor(id, @"HDAUDIO\FUNC_01&VEN_1002"))
            return 3;

        if (BusVendor(id, @"PCI\VEN_17CB") ||
            BusVendor(id, @"PCI\VEN_168C") ||
            BusVendor(id, @"USB\VID_0CF3"))
            return 4;

        if (BusVendor(id, @"PCI\VEN_14E4") ||
            BusVendor(id, @"USB\VID_0A5C"))
            return 5;

        if (BusVendor(id, @"PCI\VEN_14C3") ||
            BusVendor(id, @"USB\VID_0E8D"))
            return 6;

        return -1;
    }

    private static bool BusVendor(string id, string prefix) =>
        id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        (id.Length == prefix.Length || id[prefix.Length] == '&');
}
