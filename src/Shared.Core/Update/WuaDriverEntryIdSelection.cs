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
    // reserve space for already-declared late major-vendor IDs and
    // diverse specific PnP models from other hardware manufacturers.
    public const int ReservedVendorSlots = 64;
    // Separate audio codec buses from the open-ended pool of rare
    // PCI/USB/HID manufacturer families. Otherwise 64 distinct uncommon
    // PCI vendors can displace a valid late SNDW/ACP codec model.
    private const int OtherSpecificVendorGroup = 10;

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
        // Bucket 7 protects distinct SoundWire SDCA+MAN codec families,
        // 8/9 reserve one round-robin chance for validated SNDW and ACP
        // codecs separately. The last
        // bucket covers other *specific* PCI/USB/audio/SWC and HID VID+PID
        // models. Over 64 distinct rare PCI vendors must not bury the two
        // audio buses at the end of a long WUA entry list.
        var buckets = new List<string>[OtherSpecificVendorGroup + 1];
        for (var group = 0; group < buckets.Length; group++)
            buckets[group] = [];
        var representedSpecificFamilies = selected
            .Select(GetSpecificModelFamily)
            .Where(family => family is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A major vendor can expose several distinct device buses in a
        // single large WUA entry collection: NVIDIA PCI GPU, HDAUDIO,
        // SWC, USB and ACPI platform nodes, or Intel PCI/USB/audio/SWC.
        // Remember which *validated* bus is already represented among
        // the immutable first 960 IDs before allocating the fair slots.
        var representedMajorBuses = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var id in selected)
        {
            var group = GetVendorGroup(id);
            if (group is >= 0 and <= 6)
                representedMajorBuses.Add(MajorBusKey(group, id));
        }
        var repeatedMajorIds = new List<string>[7];
        for (var group = 0; group < repeatedMajorIds.Length; group++)
            repeatedMajorIds[group] = [];
        // Preserve family-first fairness inside ALL specific-model
        // buckets: SDCA, SNDW, ACP, and other vendors. A burst of many
        // FUNC tokens from one SoundWire SDCA+MAN family must not hide a
        // different SDCA+MAN family, just as SNDW PART repetitions cannot
        // hide another SNDW family.
        var repeatedSpecificIds = new List<string>[4] { [], [], [], [] };

        // All raw strings are validated and de-duplicated by the WUA COM
        // reader before this method is called. Use fixed bus+vendor tokens
        // from the actual Hardware ID, NEVER update title/model text.
        foreach (var id in validatedDistinctIds.Skip(baseline))
        {
            var group = GetVendorGroup(id);
            if (group < 0 || seen.Contains(id))
                continue;

            if (group is >= 0 and <= 6)
            {
                // One representative per *actual* bus for this major
                // vendor before the repeated PCI (or USB) flood. Do not
                // invent a device or change exact matching: GetVendorGroup
                // already validated the complete model above.
                if (representedMajorBuses.Add(MajorBusKey(group, id)))
                    buckets[group].Add(id);
                else
                    repeatedMajorIds[group].Add(id);
            }
            else if (group >= 7)
            {
                // All four groups require strict, specific models.
                // Represent distinct SDCA/SNDW/ACP/rare-vendor families
                // before repeated FUNC/PART/device variants. The shared
                // family key includes bus, SDCA class and manufacturer,
                // so one class/maker cannot evict the others.
                var family = GetSpecificModelFamily(id)!;
                if (representedSpecificFamilies.Add(family))
                    buckets[group].Add(id);
                else
                    repeatedSpecificIds[group - 7].Add(id);
            }
        }
        for (var group = 0; group < repeatedMajorIds.Length; group++)
            buckets[group].AddRange(repeatedMajorIds[group]);
        for (var group = 7; group <= OtherSpecificVendorGroup; group++)
            buckets[group].AddRange(repeatedSpecificIds[group - 7]);

        // One ID per group per round, so floods from any single manufacturer
        // cannot hide NVIDIA/Killer/Realtek/SoundWire or rare models.
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

    // This is a ranking key only for already validated major-vendor WUA
    // Hardware IDs. Both the group and the original bus are included:
    // NVIDIA PCI is not the same family as Intel PCI or NVIDIA HDAUDIO.
    // We never derive installability, version or a new model from this key.
    private static string MajorBusKey(int group, string id)
    {
        var separator = id.IndexOf('\\');
        return group + ":" + id[..separator].ToUpperInvariant();
    }

    // NVIDIA / Intel-Killer / Realtek / AMD / Qualcomm-Atheros / Broadcom /
    // MediaTek, plus SoundWire SDCA, SNDW, ACP and a diverse
    // other-manufacturer group (specific PCI/USB/audio/SWC and HID VID+PID).
    // Only validated
    // bus+vendor+model IDs receive reserved transport slots. Exact ID
    // equality with the Windows PnP inventory is still required later;
    // classification never proves applicability or installability.
    private static int GetVendorGroup(string id)
    {
        // The two modern audio buses need their own fair turns, outside
        // the unbounded rare-vendor family pool. Do NOT allocate a reserved
        // slot to SNDW CLASS/MAN-only or ACP DEVTYPE/DEVREV-only hints.
        // Preserve the complete original ID for exact WUA/PnP matching.
        if (id.StartsWith(@"SNDW\", StringComparison.OrdinalIgnoreCase))
            return ModernAudioPnpMatchPolicy.IsSpecificHardwareId(id) ? 8 : -1;
        if (id.StartsWith(@"ACP\", StringComparison.OrdinalIgnoreCase))
            return ModernAudioPnpMatchPolicy.IsSpecificHardwareId(id) ? 9 : -1;

        // The WUA entry reader validates strings, not a full PnP model.
        // A prefix such as PCI\\VEN_10DE&FAKE_... must NOT consume one of
        // the 64 vendor slots and hide a genuine late NVIDIA/Killer ID.
        // Reuse the strict read-only public-model parser; this does not
        // turn a WUA entry into a compatible or installable driver.
        var model = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id);
        if (model is null)
            return GetSpecificModelFamily(id) is null ? -1 : OtherSpecificVendorGroup;

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

        return GetSpecificModelFamily(id) is null ? -1 : OtherSpecificVendorGroup;
    }

    /// <summary>
    /// Distinct public bus+vendor family, never an inferred device match.
    /// Specific HID VID+PID is validated by its own strict grammar; the
    /// public Catalog parser already validates PCI/USB/audio/SWC models.
    /// Neither generic class tokens nor vendor-only IDs qualify.
    /// </summary>
    private static string? GetSpecificModelFamily(string id)
    {
        // The full, strictly validated SDCA+MAN+FUNC SoundWire model can
        // have hundreds of FUNC variants from one maker. Prioritize one
        // representative per SDCA+MAN family before those repetitions.
        // Generic aggregators, MAN-only tokens and instance paths have
        // no eligible family and never acquire a reserved slot.
        if (id.StartsWith(@"SOUNDWIRE\", StringComparison.OrdinalIgnoreCase))
        {
            var model = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id);
            if (model is null ||
                !model.StartsWith(@"SOUNDWIRE\SDCA_", StringComparison.Ordinal) ||
                !DriverUpdateMatcher.IsCorrelatableHardwareId(id))
                return null;
            var function = model.IndexOf("&FUNC_", StringComparison.Ordinal);
            return function > 0 ? model[..function] : null;
        }

        // SNDW and AMD ACP are genuine PnP audio buses, but deliberately
        // have no browser-only public Catalog query. The old fallback
        // looked only for PCI/USB &DEV_ or &PID_ and silently discarded
        // their exact WUA IDs when a large entry list exceeded IPC's
        // 1024-ID limit. Require the SAME complete, strict model grammar
        // used by the WUA/INF matcher before allocating any fair slot.
        // Shared families win before hundreds of repeat codec variants.
        if (ModernAudioPnpMatchPolicy.IsSpecificHardwareId(id))
        {
            var audioModel = id.ToUpperInvariant();
            var separator = audioModel.StartsWith(@"SNDW\",
                StringComparison.Ordinal)
                ? "&PART_" : "&DEV_";
            var familyEnd = audioModel.IndexOf(
                separator, StringComparison.Ordinal);
            return familyEnd > 0 ? audioModel[..familyEnd] : null;
        }

        if (id.StartsWith(@"HID\VID_", StringComparison.OrdinalIgnoreCase))
        {
            return HidPnpMatchPolicy.IsSpecificHardwareId(id)
                ? id[..(@"HID\VID_".Length + 4)].ToUpperInvariant()
                : null;
        }

        var model = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id);
        if (model is null ||
            !DriverUpdateMatcher.IsCorrelatableHardwareId(id))
            return null;

        // For all other supported model buses the vendor identifier
        // precedes DEV_ or PID_. SoundWire has its own protected bucket;
        // ACPI NVDA has NVIDIA's bucket; neither belongs to "other".
        var delimiter = model.StartsWith(@"USB\", StringComparison.Ordinal)
            ? "&PID_" : "&DEV_";
        var index = model.IndexOf(delimiter, StringComparison.Ordinal);
        return index > 0 ? model[..index] : null;
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
