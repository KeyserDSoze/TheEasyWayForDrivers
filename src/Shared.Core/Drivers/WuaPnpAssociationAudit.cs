using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Aggregated, privacy-safe QA of WUA Type=Driver GUIDs against the local
/// PnP inventory. This class does not search catalogs, select driver ranks,
/// authorize installation or disclose any individual device/update identity.
/// </summary>
public static class WuaPnpAssociationAudit
{
    private const int MaximumDevices = 2048;
    private const int MaximumUpdates = 4000;
    // A copied QA report runs synchronously in the desktop interaction.
    // Index each PnP ID once instead of multiplying device × update counts.
    // The budgets bound input work/memory even for malformed IPC snapshots.
    private const long MaximumPnpIdentifiers = 262_144;
    private const long MaximumWuaIdentifiers = 2_000_000;

    public sealed record Result(
        bool SearchVerified,
        bool InventoryVerified,
        bool SourcePartial,
        bool OverBudget,
        int ConfirmedGuids,
        int ExactHardware,
        int CompatibleOnly,
        int GenericModelOnly,
        int Unattributed,
        int MissingWuaIds,
        int IncompleteWuaIds)
    {
        public bool IsInstallable => false;
        public bool IsOemUpdateProof => false;

        public string Text
        {
            get
            {
                if (!SearchVerified)
                    return "Diagnostica WUA→PnP: ricerca Type=Driver non verificata; " +
                        "nessun conteggio dei driver disponibili deducibile.";
                if (OverBudget)
                    return "Diagnostica WUA→PnP: input oltre il limite; " +
                        "associazioni non valutabili (nessuna conclusione sull'assenza di driver).";
                var suffix = SourcePartial
                    ? " Alcune fonti WUA non sono verificate: risultati parziali."
                    : " Solo fonti WUA interrogate: nessuna conclusione sui cataloghi OEM.";
                if (!InventoryVerified)
                    return $"Diagnostica WUA→PnP: {ConfirmedGuids} GUID Type=Driver " +
                        "confermati da WUA; inventario PnP non verificato, " +
                        "associazioni non valutabili." + suffix;
                return $"Diagnostica WUA→PnP (per GUID Type=Driver): {ConfirmedGuids} offerte " +
                    $"confermate; {ExactHardware} con Hardware ID esatto, " +
                    $"{CompatibleOnly} solo Compatible ID, " +
                    $"{GenericModelOnly} solo modello generico (indizio non installabile), " +
                    $"{Unattributed} non attribuite a dispositivi PnP; " +
                    $"{MissingWuaIds} senza ID hardware WUA, " +
                    $"{IncompleteWuaIds} con elenco ID WUA incompleto." +
                    suffix + " Associazione non significa aggiornamento necessario o installabile.";
            }
        }
    }

    public static Result Create(
        IEnumerable<DriverInfo> devices,
        DriverSearchReport? search,
        bool inventoryComplete)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (search is null || search.AllFailed)
            return new(false, false, false, false, 0, 0, 0, 0, 0, 0, 0);

        // The real service bounds WUA updates and PnP ID arrays, but an
        // old or malformed IPC snapshot may exceed those limits. Fail closed
        // on identifier volume, NOT on a harmless device × GUID product.
        var rows = devices.Take(MaximumDevices + 1).ToArray();
        var pnpIdCount = rows.Sum(device =>
            (long)device.HardwareIds.Count + device.CompatibleIds.Count);
        var wuaIdCount = search.Updates.Sum(update =>
            1L + Math.Min((long)(update.AdditionalHardwareIds?.Count ?? 0),
                WuaDriverEntryLimits.MaximumEntriesToInspect));
        if (rows.Length > MaximumDevices || search.Updates.Count > MaximumUpdates ||
            pnpIdCount > MaximumPnpIdentifiers ||
            wuaIdCount > MaximumWuaIdentifiers)
            return new(true, false, search.IsPartial, true, 0, 0, 0, 0, 0, 0, 0);

        var byGuid = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        // Same GUID from two *completed* WUA passes can have different PnP
        // entries. Merge associations by GUID, not by title, to avoid
        // counting an update twice or losing a later NVIDIA/Killer ID.
        WindowsDriverCandidateMerge.Merge(byGuid,
            search.Updates.Where(item => Guid.TryParse(item.Id, out _)));
        var distinct = byGuid.Values.ToArray();
        var total = distinct.Length;
        if (!inventoryComplete)
            return new(true, false, search.IsPartial, false, total, 0, 0, 0, 0,
                0, distinct.Count(update => update.AdditionalHardwareIdsIncomplete));

        var pnp = rows
            .Where(device => !string.IsNullOrWhiteSpace(device.DeviceId))
            .DistinctBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Exact/compatible PnP lookup is O(device IDs + WUA IDs),
        // not O(devices × WUA GUIDs × per-update IDs). This keeps
        // realistic 500-device / 101-update inventories diagnosable.
        var exactIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var compatibleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var publicModels = new Dictionary<string, DriverInfo>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var device in pnp)
        {
            foreach (var id in device.HardwareIds)
                if (!string.IsNullOrWhiteSpace(id))
                    exactIds.Add(id.Trim());
            foreach (var id in device.CompatibleIds)
                if (!string.IsNullOrWhiteSpace(id))
                    compatibleIds.Add(id.Trim());

            // Only Hardware IDs with a validated, strictly more generic
            // model key can provide weak REVIEW evidence. Never join on
            // title, provider, Device Instance ID, or OEM substring.
            foreach (var key in WuaGenericModelCorrelation.PublicReviewModelKeys(device))
                publicModels.TryAdd(key, device);
        }

        int exact = 0, compatible = 0, weak = 0, unmatched = 0, noIds = 0;
        foreach (var update in distinct)
        {
            var declared = new[] { update.HardwareId }
                .Concat((update.AdditionalHardwareIds ?? [])
                    .Take(WuaDriverEntryLimits.MaximumAdditionalIds))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (declared.Length == 0)
                noIds++;

            if (declared.Any(exactIds.Contains))
                exact++;
            else if (declared.Any(compatibleIds.Contains))
                compatible++;
            else if (declared.Any(id =>
                publicModels.TryGetValue(id, out var device) &&
                WuaGenericModelCorrelation.IsWeakModelOnlyMatch(device, update)))
                weak++;
            else
                unmatched++;
        }

        return new(true, true, search.IsPartial, false, total,
            exact, compatible, weak, unmatched, noIds,
            distinct.Count(update => update.AdditionalHardwareIdsIncomplete));
    }
}
