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
    bool WuaCoveragePartial = false)
{
    public string DisplayLine =>
        $"{Vendor} · {Device}: installato {InstalledVersion}; " +
        (!WuaWasQueried
            ? "ricerca Windows Update non verificata; disponibilità sconosciuta."
            : CorrelatedWuaCandidates > 0
                ? $"{CorrelatedWuaCandidates} candidati Windows Update per ID " +
                  "(compatibilità da verificare)." +
                  (WuaCoveragePartial
                      ? " Ricerca WUA parziale: altre fonti non verificate."
                      : "")
                : WuaCoveragePartial
                    ? "0 correlazioni per ID dalle fonti WUA riuscite; " +
                      "ricerca WUA parziale, disponibilità driver e stato OEM non verificati."
                    : "nessun candidato Windows Update correlato per ID; stato OEM non verificato.");
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
        bool wuaCoveragePartial = false)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        ArgumentNullException.ThrowIfNull(updates);

        var limit = Math.Clamp(maximum, 0, 80);
        if (limit == 0)
            return [];

        // Several completed WUA services can return the same update GUID.
        // Count a GUID once for a device, not once per search pass.
        var candidates = WuaDiagnosticAssociations.Create(updates);

        var groups = drivers
            .Where(driver => !string.IsNullOrWhiteSpace(driver.DeviceId))
            .Where(IsRelevantDevice)
            // An upstream WMI view can repeat the same instance. Do not
            // consume two diagnostic slots or double-count its candidates.
            .DistinctBy(driver => driver.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(driver => new { Driver = driver, Vendor = ResolveVendor(driver) })
            .Where(candidate => candidate.Vendor is not null)
            .Select(candidate => new EvidenceCandidate(
                candidate.Driver, candidate.Vendor!,
                wuaWasQueried
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

        return selected.Select(row => new VendorDriverEvidence(
            row.Vendor,
            row.Driver.Name,
            string.IsNullOrWhiteSpace(row.Driver.DriverVersion)
                ? "non disponibile"
                : row.Driver.DriverVersion,
            row.WuaMatches,
            wuaWasQueried,
            wuaCoveragePartial && wuaWasQueried)).ToArray();
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
                   PciClassIdentity.HasDisplayClass(driver, "8086") ||
                   PciClassIdentity.HasNetworkClass(driver, "8086");

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
            "Qualcomm / Atheros" or "MediaTek")
            return supportVendor;

        if (evidence.Contains("Killer", StringComparison.OrdinalIgnoreCase) &&
            !VendorSourceDiscovery.HasForeignPciVendor(driver, "8086"))
            return "Killer / Intel";

        if (NvidiaHardwareDetector.IsNvidiaDevice(driver) ||
            evidence.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            return "NVIDIA";

        if (AmdHardwareDetector.IsAmdDevice(driver) ||
            evidence.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            return "AMD";

        if (IntelHardwareDetector.IsIntelDevice(driver) ||
            evidence.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return "Intel";

        return null;
    }
}
