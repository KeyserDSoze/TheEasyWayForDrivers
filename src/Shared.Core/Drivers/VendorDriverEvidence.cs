using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Read-only installed-version evidence for common Intel/NVIDIA/Killer components.
/// An absent WUA match never establishes that a device is up-to-date.
/// </summary>
public sealed record VendorDriverEvidence(
    string Vendor,
    string Device,
    string InstalledVersion,
    int CorrelatedWuaCandidates,
    bool WuaWasQueried = true,
    bool WuaCoveragePartial = false,
    bool WuaIdsIncomplete = false,
    bool WuaCorrelationsComplete = true)
{
    public string DisplayLine =>
        $"{Vendor} · {Device}: installato {InstalledVersion}; " +
        (!WuaWasQueried
            ? "ricerca Windows Update non verificata; disponibilità sconosciuta."
            : !WuaCorrelationsComplete
                ? "correlazioni Windows Update NON VALUTABILI oltre i limiti " +
                  "diagnostici; nessuna conclusione sui driver disponibili."
            : CorrelatedWuaCandidates > 0
                ? $"{CorrelatedWuaCandidates} candidati Windows Update per ID " +
                  "(compatibilità da verificare)." +
                  (WuaIdsIncomplete
                      ? " ID WUA incompleti: ulteriori associazioni possibili."
                      : "") +
                  (WuaCoveragePartial
                      ? " Ricerca WUA parziale: altre fonti non verificate."
                      : "")
                : WuaCoveragePartial
                    ? "0 correlazioni per ID dalle fonti WUA riuscite; " +
                      "ricerca WUA parziale, disponibilità driver e stato OEM non verificati." +
                      (WuaIdsIncomplete
                          ? " ID WUA incompleti: assenza non verificabile."
                          : "")
                    : WuaIdsIncomplete
                        ? "0 correlazioni WUA osservate per ID; " +
                          "ID WUA incompleti: assenza non verificabile; " +
                          "stato OEM non verificato."
                        : "nessun candidato Windows Update correlato per ID; stato OEM non verificato.");
}

/// <summary>
/// Bounded installed-driver evidence for the guided Home panel. A rejected
/// PnP inventory must never masquerade as a verified empty inventory;
/// an over-budget WUA snapshot cannot establish zero per-device matches.
/// </summary>
public sealed record VendorDriverEvidencePreview(
    IReadOnlyList<VendorDriverEvidence> Rows,
    bool InventoryComplete,
    bool CorrelationsComplete)
{
    public string CoverageNotice => !InventoryComplete
        ? "Versioni installate NON VALUTABILI: inventario PnP oltre i " +
          "limiti diagnostici; nessuna conclusione su dispositivi o driver."
        : !CorrelationsComplete
            ? "Correlazioni Windows Update NON VALUTABILI oltre i limiti: " +
              "versioni locali mostrate solo per informazione, " +
              "nessuna conclusione su aggiornamenti disponibili."
            : string.Empty;
}

public static class VendorDriverEvidenceFinder
{
    private static readonly string[] RelevantTerms =
    [
        "Graphics", "Display", "GeForce", "Radeon",
        "Bluetooth", "Wireless", "Wi-Fi", "WiFi",
        "Killer", "PROSet", "High Definition Audio",
        "Platform Controllers", "Network Adapter"
    ];

    public static IReadOnlyList<VendorDriverEvidence> Find(
        IEnumerable<DriverInfo> drivers,
        IEnumerable<DriverUpdateInfo> updates,
        int maximum = 50,
        bool wuaWasQueried = true,
        bool wuaCoveragePartial = false) =>
        FindWithCoverage(drivers, updates, maximum, wuaWasQueried,
            wuaCoveragePartial).Rows;

    public static VendorDriverEvidencePreview FindWithCoverage(
        IEnumerable<DriverInfo> drivers,
        IEnumerable<DriverUpdateInfo> updates,
        int maximum = 50,
        bool wuaWasQueried = true,
        bool wuaCoveragePartial = false)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        ArgumentNullException.ThrowIfNull(updates);

        var limit = Math.Clamp(maximum, 0, 80);
        if (limit == 0)
            return new([], true, true);

        // #591: apply the same raw, distinct-instance and ID budgets as
        // the 8-category benchmark BEFORE PnP consensus or WUA indexing.
        // Never enumerate beyond one extra raw row for either input.
        var boundedDrivers = drivers.Take(
            VendorInventoryCoverage.MaximumRawPnpRows + 1).ToArray();
        if (ReferenceDriverCoverage.ExceedsDiagnosticBudget(
                boundedDrivers, null))
            return new([], false, false);

        // A WUA failure/unqueried source must never enumerate stale rows.
        // A too-large WUA input may preserve local installed versions,
        // but cannot produce any measured WUA zero or claimed matches.
        var boundedUpdates = wuaWasQueried
            ? updates.Take(
                VendorInventoryCoverage.MaximumDriverUpdates + 1).ToArray()
            : [];
        var correlationsComplete = boundedUpdates.Length <=
            VendorInventoryCoverage.MaximumDriverUpdates;
        long wuaIdCount = 0;
        if (correlationsComplete)
            foreach (var update in boundedUpdates)
            {
                wuaIdCount += 1L + Math.Min(
                    (long)(update.AdditionalHardwareIds?.Count ?? 0),
                    WuaDriverEntryLimits.MaximumEntriesToInspect);
                if (wuaIdCount > VendorInventoryCoverage.MaximumWuaIds)
                {
                    correlationsComplete = false;
                    break;
                }
            }

        // Several completed WUA services can return the same update GUID.
        // Count a GUID once for a device, not once per search pass.
        IReadOnlyList<WuaDiagnosticAssociation> candidates =
            wuaWasQueried && correlationsComplete
                ? WuaDiagnosticAssociations.Create(boundedUpdates)
                : [];
        // Use the POST-MERGE ID-completeness state: two completed WUA passes
        // can describe the same GUID; invalid IDs discarded during the
        // merge must not turn an observed zero into a verified absence.
        // Foreign/OEM source rows are excluded by WuaDiagnosticAssociations.
        var wuaIdsIncomplete = wuaWasQueried && candidates.Any(
            association => association.AdditionalHardwareIdsIncomplete);

        // #542: WMI and SignedDriver may emit several records for one
        // PnP instance. DistinctBy chose an arbitrary FIRST record after
        // relevance filtering and could show an NVIDIA/WUA association
        // even when another row had a conflicting Realtek/Intel model.
        // Use the SAME order-independent identity consensus as Home,
        // OEM support and the eight-row benchmark before any filtering.
        // One generic first row may then recover a corroborated specific
        // same-instance model, but no IDs are merged across records.
        var groups = PnpDistinctTrustedDevices.Create(boundedDrivers)
            // WMI/IPC friendly names and version strings are untrusted
            // display data. Sanitize BEFORE keyword-based vendor routing
            // as well as before constructing diagnostic DisplayLine (#590).
            // Preserve the original PnP IDs for exact WUA associations.
            .Select(driver => driver with
            {
                Name = PnpDeviceDisplayName.Create(
                    driver.Name, driver.DeviceClass, driver.DeviceId),
                DriverVersion = InstalledDriverVersionDisplay.Create(
                    driver.DriverVersion)
            })
            .Where(IsRelevantDevice)
            .Select(driver => new { Driver = driver, Vendor = ResolveVendor(driver) })
            .Where(candidate => candidate.Vendor is not null)
            .Select(candidate => new EvidenceCandidate(
                candidate.Driver, candidate.Vendor!,
                wuaWasQueried && correlationsComplete
                    ? candidates.Count(update =>
                        update.Matches(candidate.Driver))
                    : 0))
            .GroupBy(candidate => candidate.Vendor, StringComparer.OrdinalIgnoreCase)
            // If fewer places than vendors exist, display vendors with WUA
            // correlations / Windows problems before vendors without either.
            .OrderByDescending(group => group.Any(row => row.WuaMatches > 0))
            .ThenByDescending(group => group.Any(row => row.Driver.NeedsAttention))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(row => row.WuaMatches > 0)
                .ThenByDescending(row => row.Driver.NeedsAttention)
                .ThenBy(row => row.Driver.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Driver.DeviceId, StringComparer.OrdinalIgnoreCase)
                .ToArray())
            .ToArray();

        // Alphabetical Take(50) could silently hide NVIDIA when AMD/Intel
        // filled the first 50 diagnostic rows. Round-robin uses the same
        // bounded budget while giving every detected vendor a fair share.
        var selected = new List<EvidenceCandidate>(limit);
        for (var depth = 0; selected.Count < limit; depth++)
        {
            var found = false;
            foreach (var group in groups)
            {
                if (depth >= group.Length)
                    continue;
                found = true;
                selected.Add(group[depth]);
                if (selected.Count == limit)
                    break;
            }

            if (!found)
                break;
        }

        var rows = selected.Select(row => new VendorDriverEvidence(
            row.Vendor,
            row.Driver.Name,
            InstalledDriverVersionDisplay.Create(row.Driver.DriverVersion),
            row.WuaMatches,
            wuaWasQueried,
            wuaCoveragePartial && wuaWasQueried,
            wuaIdsIncomplete,
            correlationsComplete)).ToArray();
        return new(rows, true, correlationsComplete);
    }

    private sealed record EvidenceCandidate(
        DriverInfo Driver, string Vendor, int WuaMatches);

    // Windows may show a generic Microsoft device title before the vendor
    // driver is present. Use the validated PnP vendor identity AND an
    // actionable device class, not just brand keywords in the title.
    // Broad Intel/AMD chipset bridge classes are intentionally excluded.
    private static bool IsRelevantDevice(DriverInfo driver)
    {
        // PCI infrastructure is not a meaningful update suggestion,
        // even if an upstream inventory mistakenly assigns a broad class.
        if (driver.Name.Contains("Host Bridge", StringComparison.OrdinalIgnoreCase) ||
            driver.Name.Contains("Root Port", StringComparison.OrdinalIgnoreCase))
            return false;

        if (RelevantTerms.Any(term =>
            driver.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            return true;

        static bool IsClass(DriverInfo item, string value) =>
            string.Equals(item.DeviceClass, value,
                StringComparison.OrdinalIgnoreCase);

        // Check the PnP-aware support routing FIRST. A Realtek codec on
        // Intel SST can legitimately report Intel as a driver provider;
        // that must not hide its Realtek device classification.
        if (VendorSourceDiscovery.GetRecognizedSupportVendor(driver) is
            "Killer / Intel" or "Realtek" or "Broadcom" or
            "Qualcomm / Atheros" or "MediaTek")
            return true;

        if (NvidiaHardwareDetector.IsNvidiaDevice(driver))
            return IsClass(driver, "Display") || IsClass(driver, "MEDIA") ||
                   IsClass(driver, "System") ||
                   IsClass(driver, "SoftwareComponent") ||
                   PciClassIdentity.HasDisplayClass(driver, "10DE");

        if (IntelHardwareDetector.IsIntelDevice(driver))
            return IsClass(driver, "Display") || IsClass(driver, "Net") ||
                   IsClass(driver, "Bluetooth") ||
                   SwcVendorComponentIdentity.IsIntel(driver) ||
                   IntelHardwareDetector.HasIntelDisplayClass(driver) ||
                   IntelHardwareDetector.HasIntelNetworkClass(driver);

        return AmdHardwareDetector.IsAmdDevice(driver) &&
               IsClass(driver, "Display");
    }

    private static string? ResolveVendor(DriverInfo driver)
    {
        var evidence = string.Join(" ", new[]
        {
            driver.Name,
            driver.Manufacturer ?? string.Empty,
            driver.DriverProvider ?? string.Empty
        });

        // PnP vendor identity outranks a marketing label in the installed
        // driver/provider. Do not call a Realtek/Qualcomm/NVIDIA device
        // "Killer / Intel" solely because its title includes "Killer".
        var supportVendor = VendorSourceDiscovery.GetRecognizedSupportVendor(driver);
        if (supportVendor is
            "Killer / Intel" or "Realtek" or "Broadcom" or
            "Qualcomm / Atheros" or "MediaTek" or
            "NVIDIA" or "Intel" or "AMD")
            return supportVendor;

        if (evidence.Contains("Killer", StringComparison.OrdinalIgnoreCase) &&
            !VendorSourceDiscovery.HasForeignPciVendor(driver, "8086", "1A56"))
            return "Killer / Intel";

        // The source above has already considered valid PCI models. Do
        // not reintroduce a discredited label via an evidence-only fallback.
        if ((NvidiaHardwareDetector.IsNvidiaDevice(driver) ||
             evidence.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) &&
            !VendorSourceDiscovery.HasForeignPciVendor(driver, "10DE"))
            return "NVIDIA";

        if ((AmdHardwareDetector.IsAmdDevice(driver) ||
             evidence.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) &&
            !VendorSourceDiscovery.HasForeignPciVendor(driver, "1002"))
            return "AMD";

        if ((IntelHardwareDetector.IsIntelDevice(driver) ||
             evidence.Contains("Intel", StringComparison.OrdinalIgnoreCase)) &&
            !VendorSourceDiscovery.HasForeignPciVendor(driver, "8086", "1A56"))
            return "Intel";

        return null;
    }
}
