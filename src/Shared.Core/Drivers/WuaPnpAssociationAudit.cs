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
    // Even individually bounded WUA IDs and PnP nodes could form millions
    // of pairwise comparisons on unusual inventories. Fail closed rather
    // than freezing the home screen or showing a partial count as complete.
    private const long MaximumDeviceUpdatePairs = 50_000;

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

        // Avoid unbounded work on malformed IPC inputs. The real service
        // already bounds the raw WUA UpdateCollection to 4000 per pass.
        var rows = devices.Take(MaximumDevices + 1).ToArray();
        if (rows.Length > MaximumDevices || search.Updates.Count > MaximumUpdates ||
            (long)rows.Length * search.Updates.Count > MaximumDeviceUpdatePairs)
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

        int exact = 0, compatible = 0, weak = 0, unmatched = 0, noIds = 0;
        foreach (var update in distinct)
        {
            var declared = !string.IsNullOrWhiteSpace(update.HardwareId) ||
                (update.AdditionalHardwareIds?.Any(id =>
                    !string.IsNullOrWhiteSpace(id)) == true);
            if (!declared)
                noIds++;

            var strength = 0;
            foreach (var device in pnp)
            {
                strength = Math.Max(strength,
                    DriverUpdateMatcher.MatchStrength(device, update));
                if (strength == 2)
                    break;
            }
            if (strength == 2)
                exact++;
            else if (strength == 1)
                compatible++;
            else if (pnp.Any(device =>
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
