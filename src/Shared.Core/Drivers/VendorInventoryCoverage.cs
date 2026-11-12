using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Counts recognized vendor devices from the *whole* verified PnP inventory,
/// never from the capped list of OEM support links. The correlations are
/// existing WUA Type=Driver GUIDs matched by exact PnP IDs, not offers
/// authenticated by a manufacturer or authority to install.
/// </summary>
public static class VendorInventoryCoverage
{
    public const int MaximumDevices = 2048;
    public const int MaximumDriverUpdates = 4000;
    public const long MaximumPnpIds = 262_144;
    public const long MaximumWuaIds = 2_000_000;
    public const long MaximumMatchedPairs = 250_000;

    public sealed record Report(
        IReadOnlyList<VendorCoverageRow> Rows,
        bool InventoryComplete,
        bool CorrelationsComplete,
        bool WuaSourcesPartial = false)
    {
        public string ScopeNote =>
            !InventoryComplete
                ? "Inventario PnP oltre il limite: conteggi parziali, nessuna " +
                  "conclusione sulle periferiche non esaminate."
                : !CorrelationsComplete
                    ? "Dispositivi contati da inventario PnP; associazioni " +
                      "WUA non valutabili oltre i limiti, NON significa zero driver."
                    : "Dispositivi contati dall'inventario PnP completo, " +
                      "indipendentemente dal limite di 100 collegamenti OEM " +
                      "mostrati nella UI. Non prova aggiornamenti disponibili." +
                      (WuaSourcesPartial
                          ? " Ricerca WUA Type=Driver PARZIALE: " +
                            "associazioni note solo dalle fonti completate; " +
                            "zero non prova assenza di ulteriori driver."
                          : "");
    }

    public static Report Create(
        IReadOnlyList<DriverInfo> devices,
        IReadOnlyList<DriverUpdateInfo> confirmedWuaUpdates,
        bool wuaSourcesPartial = false)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(confirmedWuaUpdates);

        var bounded = devices.Take(MaximumDevices + 1).ToArray();
        var inventoryComplete = bounded.Length <= MaximumDevices;
        var recognized = PnpDistinctTrustedDevices
            .Create(bounded.Take(MaximumDevices))
            .Select(device => (Device: device,
                Vendor: VendorSourceDiscovery.GetRecognizedSupportVendor(device)))
            .Where(item => item.Vendor is not null)
            .Select(item => (item.Device, Vendor: item.Vendor!))
            .ToArray();

        var correlated = new int[recognized.Length];
        var correlationsComplete = true;

        // Existing confirmed WUA updates ONLY, never cached/partial hints,
        // vendor support links, Type=Software or inferred OEM downloads.
        // Reject over-budget IPC data BEFORE the GUID merge or ID index.
        var wuaIdCount = confirmedWuaUpdates.Sum(update =>
            1L + Math.Min((long)(update.AdditionalHardwareIds?.Count ?? 0),
                WuaDriverEntryLimits.MaximumAdditionalIds));
        if (confirmedWuaUpdates.Count > MaximumDriverUpdates ||
            wuaIdCount > MaximumWuaIds)
        {
            correlationsComplete = false;
        }
        else
        {
            var byId = new Dictionary<string, List<int>>(
                StringComparer.OrdinalIgnoreCase);
            long pnpCount = 0;
            for (var index = 0; index < recognized.Length; index++)
            {
                var device = recognized[index].Device;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in device.HardwareIds.Concat(device.CompatibleIds))
                {
                    if (++pnpCount > MaximumPnpIds)
                    {
                        correlationsComplete = false;
                        break;
                    }
                    if (!DriverUpdateMatcher.IsCorrelatableHardwareId(id))
                        continue;
                    var key = id.Trim();
                    if (!seen.Add(key))
                        continue;
                    if (!byId.TryGetValue(key, out var entries))
                        byId[key] = entries = [];
                    entries.Add(index);
                }
                if (!correlationsComplete)
                    break;
            }

            if (correlationsComplete)
            {
                var confirmed = WuaDiagnosticAssociations.Create(
                    confirmedWuaUpdates.Where(update =>
                        Guid.TryParse(update.Id, out _)));
                long pairs = 0;
                foreach (var update in confirmed)
                {
                    var matched = new HashSet<int>();
                    var seenIds = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    var ids = new[] { update.PrimaryHardwareId }
                        .Concat((update.AdditionalHardwareIds ?? [])
                            .Take(WuaDriverEntryLimits.MaximumAdditionalIds));
                    foreach (var id in ids)
                    {
                        if (!DriverUpdateMatcher.IsCorrelatableHardwareId(id))
                            continue;
                        var key = id!.Trim();
                        if (!seenIds.Add(key) ||
                            !byId.TryGetValue(key, out var entries))
                            continue;
                        foreach (var index in entries)
                        {
                            if (matched.Add(index) &&
                                ++pairs > MaximumMatchedPairs)
                            {
                                correlationsComplete = false;
                                break;
                            }
                        }
                        if (!correlationsComplete)
                            break;
                    }
                    if (!correlationsComplete)
                        break;
                    foreach (var index in matched)
                        correlated[index]++;
                }
            }
        }

        // Partial association counts MUST NOT be displayed as confirmed zero.
        var rows = recognized.Select((item, index) => new
        {
            item.Vendor, item.Device,
            Correlations = correlationsComplete ? correlated[index] : 0
        })
        .GroupBy(item => item.Vendor, StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group => new VendorCoverageRow(
            group.Key,
            group.Count(),
            correlationsComplete
                ? group.Count(item => item.Correlations > 0) : 0,
            correlationsComplete
                ? group.Sum(item => item.Correlations) : 0,
            group.Count(item => string.IsNullOrWhiteSpace(
                item.Device.DriverVersion)),
            InventoryComplete: inventoryComplete,
            WuaCorrelationsComplete: correlationsComplete,
            WuaSourcesPartial: wuaSourcesPartial))
        .ToArray();
        return new Report(rows, inventoryComplete, correlationsComplete,
            wuaSourcesPartial);
    }
}
