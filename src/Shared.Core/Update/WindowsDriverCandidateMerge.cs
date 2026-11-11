using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Merges independent WUA search passes by update ID. The first confirmed
/// ordinary pass wins over an advanced result for the same update; a pass
/// failing part-way is never handed to this collector.
/// </summary>
public static class WindowsDriverCandidateMerge
{
    // The metadata from a confirmed, ordinary WUA pass must win over a
    // hidden record or unknown-origin record for the SAME update GUID.
    // If either representation identifies firmware/BIOS, fail closed:
    // a conflicting ordinary title cannot make that GUID installable.
    private static DriverUpdateInfo PreferredMetadata(
        DriverUpdateInfo existing, DriverUpdateInfo candidate)
    {
        var existingFirmware = QuickUpdatePolicy.IsFirmwareOrBios(
            existing.Title, existing.DriverClass);
        var candidateFirmware = QuickUpdatePolicy.IsFirmwareOrBios(
            candidate.Title, candidate.DriverClass);
        if (existingFirmware != candidateFirmware)
            return existingFirmware ? existing : candidate;

        var existingEligible = QuickUpdatePolicy.IsEligible(existing);
        var candidateEligible = QuickUpdatePolicy.IsEligible(candidate);
        if (existingEligible != candidateEligible)
            return candidateEligible ? candidate : existing;

        // Preserve the historical choice if neither is confirmed ordinary,
        // or both are. Optionality from another trusted WUA pass is merged
        // separately and must not disappear when ordinary metadata wins.
        // Correlation never upgrades an advanced-only GUID.
        return existing.IsAdvancedCandidate && !candidate.IsAdvancedCandidate
            ? candidate : existing;
    }

    private static bool IsKnownDriverPassSource(DriverUpdateInfo update)
    {
        if (QuickUpdatePolicy.IsKnownWindowsUpdateSource(update.SearchSource))
            return true;

        // Advanced WUA passes are valid diagnostic PnP evidence, but never
        // become ordinary offers. Unknown OEM/manual sources are not.
        const string suffix = " · avanzato";
        var source = update.SearchSource;
        return update.IsAdvancedCandidate && source is not null &&
            source.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
            QuickUpdatePolicy.IsKnownWindowsUpdateSource(
                source[..^suffix.Length]);
    }

    private static DriverUpdateInfo MergeDeviceIds(
        DriverUpdateInfo preferred, DriverUpdateInfo other)
    {
        // The default ID remains the display identity. The other pass's
        // default ID is retained among extras when it differs.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validated = new List<string>();
        var incomplete = preferred.AdditionalHardwareIdsIncomplete ||
            other.AdditionalHardwareIdsIncomplete ||
            // IPC or unit-test supplied lists must not cause unbounded
            // traversal even when all IDs are duplicates.
            (preferred.AdditionalHardwareIds?.Count ?? 0) >
                WuaDriverEntryLimits.MaximumEntriesToInspect ||
            (other.AdditionalHardwareIds?.Count ?? 0) >
                WuaDriverEntryLimits.MaximumEntriesToInspect;
        if (!string.IsNullOrWhiteSpace(preferred.HardwareId))
            known.Add(preferred.HardwareId.Trim());

        void Append(string? id)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                id.Length > WuaDriverEntryLimits.MaximumIdLength ||
                id.Any(char.IsControl))
                return;
            var trimmed = id.Trim();
            if (!known.Add(trimmed))
                return;
            // Do NOT discard the second search pass's secondary IDs
            // while the first pass fills the 1024-ID transport ceiling.
            // Each input pass is independently bounded to 2048 IDs.
            validated.Add(trimmed);
        }

        // Reserve a position for the OTHER confirmed WUA pass's primary
        // Hardware ID BEFORE secondary IDs fill the bounded list. A GUID
        // returned with 1024 secondary IDs by one pass could otherwise
        // hide a real NVIDIA/Killer/Realtek match from another WUA source.
        // Still one update GUID: this is association evidence only.
        Append(other.HardwareId);
        foreach (var id in (preferred.AdditionalHardwareIds ?? [])
            .Take(WuaDriverEntryLimits.MaximumEntriesToInspect))
            Append(id);
        foreach (var id in (other.AdditionalHardwareIds ?? [])
            .Take(WuaDriverEntryLimits.MaximumEntriesToInspect))
            Append(id);

        // The single-pass reader already selects vendor-fair ID subsets.
        // A cross-source GUID merge must make the same selection AFTER
        // combining the distinct IDs, otherwise the first pass's 1024
        // secondary entries hide NVIDIA/Killer/Realtek evidence from a
        // later confirmed pass. The other pass's primary ID was appended
        // first and remains protected among the first 960.
        var selected = WuaDriverEntryIdSelection.SelectMerged(validated);
        return preferred with
        {
            AdditionalHardwareIds = selected.HardwareIds,
            AdditionalHardwareIdsIncomplete =
                incomplete || selected.DeferredIds > 0
        };
    }

    /// <returns>Number of genuinely new update IDs added by this pass.</returns>
    public static int Merge(
        IDictionary<string, DriverUpdateInfo> destination,
        IEnumerable<DriverUpdateInfo> candidates)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(candidates);

        var added = 0;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Id))
                continue;

            if (!destination.TryGetValue(candidate.Id, out var existing))
            {
                // Normalize even newly seen WUA data; a malformed or
                // oversized IPC collection must not bypass the shared cap.
                destination[candidate.Id] = MergeDeviceIds(candidate, candidate);
                added++;
            }
            else
            {
                // A matching GUID is not evidence of a matching PROVENANCE.
                // Only a source emitted by our recognized WUA search passes
                // can contribute metadata or PnP IDs to a recognized WUA
                // record. In particular, an unknown "Firmware" title must
                // not veto a real WUA display driver, and unknown secondary
                // hardware IDs must not contaminate an advanced-only WUA
                // review. This is an ordering/provenance rule, NOT proof
                // that any WUA GUID is installable: install-time recheck
                // and QuickUpdatePolicy still apply independently.
                var existingTrusted = IsKnownDriverPassSource(existing);
                var candidateTrusted = IsKnownDriverPassSource(candidate);
                if (existingTrusted != candidateTrusted)
                {
                    if (candidateTrusted)
                        destination[candidate.Id] =
                            MergeDeviceIds(candidate, candidate);
                    // Existing trusted record is already normalized.
                    // Ignore all metadata, IDs and incomplete flags from
                    // the unrecognized source in either arrival order.
                    continue;
                }

                // One update GUID may describe more than one PnP device,
                // and different WUA passes can expose different device
                // entries. Preserve those IDs without counting extra updates.
                // Prefer a genuinely eligible Windows Update record over
                // hidden/unknown-origin metadata. Preserve valid advanced
                // WUA device IDs, but never attach untrusted IDs to an
                // ordinary offer merely because its GUID is the same.
                // A confirmed WUA GUID can be returned as recommended by
                // one WUA service and BrowseOnly by another. Treat ANY
                // trusted BrowseOnly evidence as a review-only safety veto,
                // independent of arrival order or which metadata wins.
                // Unknown OEM/manual sources cannot poison a valid WUA
                // record by reusing its GUID.
                var trustedBrowseOnly =
                    (IsKnownDriverPassSource(existing) && existing.IsOptional) ||
                    (IsKnownDriverPassSource(candidate) && candidate.IsOptional);
                var preferred = PreferredMetadata(existing, candidate);
                var other = ReferenceEquals(preferred, existing) ? candidate : existing;
                var trustedOther = QuickUpdatePolicy.IsEligible(preferred) &&
                    !IsKnownDriverPassSource(other) ? preferred : other;
                destination[candidate.Id] =
                    MergeDeviceIds(preferred, trustedOther) with
                    {
                        IsOptional = preferred.IsOptional || trustedBrowseOnly
                    };
            }
        }

        return added;
    }
}
