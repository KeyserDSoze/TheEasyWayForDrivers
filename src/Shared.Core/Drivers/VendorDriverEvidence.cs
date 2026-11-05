using TheEasyWayForDrivers.Core.Models;

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
    bool WuaWasQueried = true)
{
    public string DisplayLine =>
        $"{Vendor} · {Device}: installato {InstalledVersion}; " +
        (!WuaWasQueried
            ? "ricerca Windows Update non verificata; disponibilità sconosciuta."
            : CorrelatedWuaCandidates > 0
            ? $"{CorrelatedWuaCandidates} candidati Windows Update per ID (compatibilità da verificare)."
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
        bool wuaWasQueried = true)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        ArgumentNullException.ThrowIfNull(updates);

        var candidates = updates.ToArray();

        return drivers
            .Where(IsRelevantDevice)
            .Select(driver => new { Driver = driver, Vendor = ResolveVendor(driver) })
            .Where(candidate => candidate.Vendor is not null)
            .OrderBy(candidate => candidate.Vendor, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Driver.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maximum, 0, 80))
            .Select(candidate => new VendorDriverEvidence(
                candidate.Vendor!,
                candidate.Driver.Name,
                string.IsNullOrWhiteSpace(candidate.Driver.DriverVersion)
                    ? "non disponibile"
                    : candidate.Driver.DriverVersion,
                DriverUpdateMatcher.FindMatches(candidate.Driver, candidates).Count,
                wuaWasQueried))
            .ToArray();
    }

    // Windows may show a generic Microsoft device title before the vendor
    // driver is present. Use the validated PnP vendor identity AND an
    // actionable device class, not just brand keywords in the title.
    // Broad Intel/AMD chipset bridge classes are intentionally excluded.
    private static bool IsRelevantDevice(DriverInfo driver)
    {
        if (RelevantTerms.Any(term =>
            driver.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            return true;

        static bool IsClass(DriverInfo item, string value) =>
            string.Equals(item.DeviceClass, value,
                StringComparison.OrdinalIgnoreCase);

        if (NvidiaHardwareDetector.IsNvidiaDevice(driver))
            return IsClass(driver, "Display") || IsClass(driver, "MEDIA") ||
                   IsClass(driver, "System") ||
                   IsClass(driver, "SoftwareComponent");

        if (IntelHardwareDetector.IsIntelDevice(driver))
            return IsClass(driver, "Display") || IsClass(driver, "Net") ||
                   IsClass(driver, "Bluetooth");

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

        if (evidence.Contains("Killer", StringComparison.OrdinalIgnoreCase))
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
