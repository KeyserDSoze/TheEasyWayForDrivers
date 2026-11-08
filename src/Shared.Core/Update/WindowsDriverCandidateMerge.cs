using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Merges independent WUA search passes by update ID. The first confirmed
/// ordinary pass wins over an advanced result for the same update; a pass
/// failing part-way is never handed to this collector.
/// </summary>
public static class WindowsDriverCandidateMerge
{
    private const int MaximumDeviceIds = WuaDriverEntryLimits.MaximumAdditionalIds;

    private static DriverUpdateInfo MergeDeviceIds(
        DriverUpdateInfo preferred, DriverUpdateInfo other)
    {
        // The default ID remains the display identity. The other pass's
        // default ID is retained among extras when it differs.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extras = new List<string>();
        var incomplete = preferred.AdditionalHardwareIdsIncomplete ||
            other.AdditionalHardwareIdsIncomplete;
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
            if (extras.Count < MaximumDeviceIds)
                extras.Add(trimmed);
            else
                incomplete = true;
        }

        // Reserve a position for the OTHER confirmed WUA pass's primary
        // Hardware ID BEFORE secondary IDs fill the bounded list. A GUID
        // returned with 1024 secondary IDs by one pass could otherwise
        // hide a real NVIDIA/Killer/Realtek match from another WUA source.
        // Still one update GUID: this is association evidence only.
        Append(other.HardwareId);
        foreach (var id in preferred.AdditionalHardwareIds ?? [])
            Append(id);
        foreach (var id in other.AdditionalHardwareIds ?? [])
            Append(id);

        return preferred with
        {
            AdditionalHardwareIds = extras,
            AdditionalHardwareIdsIncomplete = incomplete
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
                // One update GUID may describe more than one PnP device,
                // and different WUA passes can expose different device
                // entries. Preserve those IDs without counting extra updates.
                // Keep ordinary results preferred over advanced metadata.
                var preferred = existing.IsAdvancedCandidate &&
                    !candidate.IsAdvancedCandidate ? candidate : existing;
                var other = ReferenceEquals(preferred, existing) ? candidate : existing;
                destination[candidate.Id] = MergeDeviceIds(preferred, other);
            }
        }

        return added;
    }
}
