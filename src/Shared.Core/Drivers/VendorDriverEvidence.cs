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
        int maximum = 12,
        bool wuaWasQueried = true)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        ArgumentNullException.ThrowIfNull(updates);

        var candidates = updates.ToArray();

        return drivers
            .Where(driver => RelevantTerms.Any(term =>
                driver.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(driver => new { Driver = driver, Vendor = ResolveVendor(driver) })
            .Where(candidate => candidate.Vendor is not null)
            .OrderBy(candidate => candidate.Vendor, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Driver.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maximum, 0, 30))
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

        if (evidence.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            driver.HardwareIds.Any(id =>
                id.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase)))
            return "NVIDIA";

        if (evidence.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            driver.HardwareIds.Any(id =>
                id.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase)))
            return "Intel";

        return null;
    }
}
