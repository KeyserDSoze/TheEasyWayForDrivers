using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Merges independent WUA search passes by update ID. The first confirmed
/// ordinary pass wins over an advanced result for the same update; a pass
/// failing part-way is never handed to this collector.
/// </summary>
public static class WindowsDriverCandidateMerge
{
    private const int MaximumDeviceIds = 128;

    private static DriverUpdateInfo MergeDeviceIds(
        DriverUpdateInfo preferred, DriverUpdateInfo other)
    {
        // The default ID remains the display identity. The other pass's
        // default ID is retained among extras when it differs.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extras = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferred.HardwareId))
            known.Add(preferred.HardwareId.Trim());

        void Append(string? id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 512 ||
                id.Any(char.IsControl))
                return;
            var trimmed = id.Trim();
            if (known.Add(trimmed) && extras.Count < MaximumDeviceIds)
                extras.Add(trimmed);
        }

        foreach (var id in preferred.AdditionalHardwareIds ?? [])
            Append(id);
        Append(other.HardwareId);
        foreach (var id in other.AdditionalHardwareIds ?? [])
            Append(id);

        return preferred with { AdditionalHardwareIds = extras };
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
                destination[candidate.Id] = candidate;
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
