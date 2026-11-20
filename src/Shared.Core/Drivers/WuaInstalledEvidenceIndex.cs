using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Bounded, exact-ID index for the read-only installed-version Home panel
/// (#592). The index narrows candidate GUIDs but NEVER decides whether an
/// update is a real PnP association: WuaDiagnosticAssociation.Matches does.
/// Weak model-only hints and OEM metadata are not indexed or counted.
/// </summary>
internal sealed class WuaInstalledEvidenceIndex
{
    internal const int MaximumIndexedAssociations = 250_000;

    private readonly IReadOnlyList<WuaDiagnosticAssociation> _candidates;
    private readonly Dictionary<string, List<int>>? _byId;

    private WuaInstalledEvidenceIndex(
        IReadOnlyList<WuaDiagnosticAssociation> candidates,
        Dictionary<string, List<int>>? byId)
    {
        _candidates = candidates;
        _byId = byId;
    }

    internal bool UsesIndex => _byId is not null;

    internal static WuaInstalledEvidenceIndex Create(
        IReadOnlyList<WuaDiagnosticAssociation> candidates,
        int maxIndexedAssociations = MaximumIndexedAssociations)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (maxIndexedAssociations < 1)
            throw new ArgumentOutOfRangeException(
                nameof(maxIndexedAssociations));

        var byId = new Dictionary<string, List<int>>(
            StringComparer.OrdinalIgnoreCase);
        var entries = 0;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            // A repeated ID in the same GUID is one association, not two.
            // The declared-ID cap mirrors DriverUpdateMatcher.MatchStrength.
            var seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var declared = new[] { candidate.PrimaryHardwareId }
                .Concat((candidate.AdditionalHardwareIds ?? [])
                    .Take(WuaDriverEntryLimits.MaximumAdditionalIds));
            foreach (var id in declared)
            {
                if (!DriverUpdateMatcher.IsCorrelatableHardwareId(id))
                    continue;

                var key = id!.Trim();
                if (!seen.Add(key))
                    continue;
                if (++entries > maxIndexedAssociations)
                    return new(candidates, null);

                if (!byId.TryGetValue(key, out var list))
                    byId[key] = list = [];
                list.Add(index);
            }
        }

        return new(candidates, byId);
    }

    internal int CountForDevice(DriverInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (_byId is null)
            return _candidates.Count(candidate => candidate.Matches(device));

        // Only strictly correlatable, exact Hardware/Compatible IDs can
        // narrow the set. Every candidate is rechecked with the original
        // MatchStrength policy to prevent new/weak associations.
        var indexes = new HashSet<int>();
        foreach (var id in device.HardwareIds.Concat(device.CompatibleIds))
        {
            if (!DriverUpdateMatcher.IsCorrelatableHardwareId(id) ||
                !_byId.TryGetValue(id!.Trim(' '), out var matches))
                continue;

            foreach (var index in matches)
                indexes.Add(index);
        }

        return indexes.Count(index => _candidates[index].Matches(device));
    }
}
