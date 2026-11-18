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
    // Keep the raw-row and distinct-instance limits synchronized with the
    // OEM vendor matrix. One WMI/SignedDriver PnP instance may have many
    // raw representations; duplicates must not consume device slots (#544).
    private const int MaximumDevices = VendorInventoryCoverage.MaximumDevices;
    private const int MaximumRawPnpRows = VendorInventoryCoverage.MaximumRawPnpRows;
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
        // Conflicting PnP vendor records for ONE DeviceId were discarded:
        // anonymous count only, never expose instance paths or brand text.
        public int ExcludedAmbiguousPnpInstances { get; init; }

        // #462: subset of Unattributed GUIDs with only class/vendor-only,
        // malformed or otherwise non-specific WUA IDs in the inspected
        // transport window. Distinct from a GUID with no IDs at all and
        // from one carrying a specific model absent from this PnP inventory.
        public int OnlyNonCorrelatableWuaIds { get; init; }

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
                    $"{GenericModelOnly} solo modello generico o stesso sottosistema senza revisione (indizi non installabili), " +
                    $"{Unattributed} non attribuite a dispositivi PnP; " +
                    $"{MissingWuaIds} senza ID hardware WUA, " +
                    $"{IncompleteWuaIds} con elenco ID WUA incompleto." +
                    suffix +
                    (OnlyNonCorrelatableWuaIds > 0
                        ? $" Di queste offerte non attribuite, " +
                          $"{OnlyNonCorrelatableWuaIds} GUID WUA dichiarano solo " +
                          "ID generici o non verificabili nella porzione letta: " +
                          "non sono associabili per Hardware ID specifico; " +
                          "non significa driver assente o aggiornamento necessario."
                        : "") +
                    (ExcludedAmbiguousPnpInstances > 0
                        ? $" {ExcludedAmbiguousPnpInstances} istanze PnP con " +
                          "identità hardware contraddittorie escluse dalla correlazione: " +
                          "assenza di driver NON verificabile."
                        : "") +
                    " Associazione non significa aggiornamento necessario o installabile.";
            }
        }
    }

    public static Result Create(
        IEnumerable<DriverInfo> devices,
        DriverSearchReport? search,
        bool inventoryComplete)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (search is null ||
            !WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(search))
            return new(false, false, false, false, 0, 0, 0, 0, 0, 0, 0);

        // The real service bounds WUA updates and PnP ID arrays, but an
        // old or malformed IPC snapshot may exceed those limits. Fail closed
        // on identifier volume, NOT on a harmless device × GUID product.
        // #544: unlike the former Take(2049) raw-row gate, the same
        // quota used by VendorInventoryCoverage admits at most 2048
        // DISTINCT PnP instances from up to 8192 raw WMI rows. Keep all
        // later representations of an admitted instance: otherwise a
        // conflicting Realtek model could leave a false NVIDIA/WUA match.
        // Never claim coverage if either input budget is exceeded.
        var rows = new List<DriverInfo>();
        var distinctInstances = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var rawRowsSeen = 0;
        var exceededPnpBudget = false;
        foreach (var device in devices)
        {
            if (++rawRowsSeen > MaximumRawPnpRows)
            {
                exceededPnpBudget = true;
                break;
            }
            if (device is null || string.IsNullOrWhiteSpace(device.DeviceId))
                continue;
            if (!distinctInstances.Contains(device.DeviceId) &&
                distinctInstances.Count == MaximumDevices)
            {
                exceededPnpBudget = true;
                break;
            }

            distinctInstances.Add(device.DeviceId);
            rows.Add(device);
        }

        var pnpIdCount = rows.Sum(device =>
            (long)device.HardwareIds.Count + device.CompatibleIds.Count);
        var wuaIdCount = search.Updates.Sum(update =>
            1L + Math.Min((long)(update.AdditionalHardwareIds?.Count ?? 0),
                WuaDriverEntryLimits.MaximumEntriesToInspect));
        if (exceededPnpBudget || search.Updates.Count > MaximumUpdates ||
            pnpIdCount > MaximumPnpIdentifiers ||
            wuaIdCount > MaximumWuaIdentifiers)
            return new(true, false, search.IsPartial, true, 0, 0, 0, 0, 0, 0, 0);

        var byGuid = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        // Same GUID from two *completed* WUA passes can have different PnP
        // entries. Merge associations by GUID, not by title, to avoid
        // counting an update twice or losing a later NVIDIA/Killer ID.
        WindowsDriverCandidateMerge.Merge(byGuid,
            WindowsDriverCandidateMerge.ConfirmedForReport(search));
        var distinct = byGuid.Values.ToArray();
        var total = distinct.Length;
        if (!inventoryComplete)
            return new(true, false, search.IsPartial, false, total, 0, 0, 0, 0,
                0, distinct.Count(update => update.AdditionalHardwareIdsIncomplete));

        // One WMI/SignedDriver instance may be duplicated with mutually
        // incompatible NVIDIA/Intel/Realtek PnP models. Home and the
        // OEM companion already discard that entire instance. Applying
        // the same policy here prevents an exact or weak WUA association
        // from depending on which row Windows returned first.
        var pnp = PnpDistinctTrustedDevices.Create(rows);
        var excludedAmbiguous = distinctInstances.Count - pnp.Count;

        // Exact/compatible PnP lookup is O(device IDs + WUA IDs),
        // not O(devices × WUA GUIDs × per-update IDs). This keeps
        // realistic 500-device / 101-update inventories diagnosable.
        var exactIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var compatibleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var publicModels = new Dictionary<string, DriverInfo>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var device in pnp)
        {
            // An unverified local WMI/IPC ID cannot become exact evidence
            // merely because trimming it matches a genuine WUA token.
            foreach (var id in device.HardwareIds)
                if (DriverUpdateMatcher.IsCorrelatableHardwareId(id))
                    exactIds.Add(id.Trim(' '));
            foreach (var id in device.CompatibleIds)
                if (DriverUpdateMatcher.IsCorrelatableHardwareId(id))
                    compatibleIds.Add(id.Trim(' '));

            // Only Hardware IDs with a validated generic or SUBSYS-minus-REV
            // model key can provide weak REVIEW evidence. Never join on
            // title, provider, Device Instance ID, or OEM substring.
            foreach (var key in WuaGenericModelCorrelation.ReviewModelKeys(device))
                publicModels.TryAdd(key, device);
        }

        int exact = 0, compatible = 0, weak = 0, unmatched = 0, noIds = 0;
        int onlyNonCorrelatable = 0;
        foreach (var update in distinct)
        {
            var declared = new[] { update.HardwareId }
                .Concat((update.AdditionalHardwareIds ?? [])
                    .Take(WuaDriverEntryLimits.MaximumAdditionalIds))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                // Only trim harmless ASCII padding. Trim() would erase
                // CR/LF/TAB from a malformed WUA ID *before* the strict
                // IsCorrelatableHardwareId gate sees the original token.
                .Select(id => id!.Trim(' '))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (declared.Length == 0)
                noIds++;

            // WUA may expose only a bus class or a vendor ID. These are
            // real GUIDs returned by a completed query but never evidence
            // of an attributable, device-specific driver. Count ONCE per
            // merged GUID, not once per ID or WUA source; an incomplete
            // WUA association array cannot attest what was omitted.
            var specific = declared
                .Where(DriverUpdateMatcher.IsCorrelatableHardwareId)
                .ToArray();
            if (declared.Length > 0 && specific.Length == 0)
                onlyNonCorrelatable++;

            if (specific.Any(exactIds.Contains))
                exact++;
            else if (specific.Any(compatibleIds.Contains))
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
            distinct.Count(update => update.AdditionalHardwareIdsIncomplete))
        {
            ExcludedAmbiguousPnpInstances = excludedAmbiguous,
            OnlyNonCorrelatableWuaIds = onlyNonCorrelatable
        };
    }
}
