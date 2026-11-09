namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Device counts are based on hardware recognized by vendor handoff rules,
/// NOT proof that any published vendor driver is an update. WUA matches are
/// per-device HardwareID/CompatibleID correlations only.
/// </summary>
public sealed record VendorCoverageRow(
    string Vendor,
    int Devices,
    int DevicesWithWuaMatches,
    int WuaCorrelations,
    int DevicesWithoutInstalledVersion,
    bool InventoryComplete = true,
    bool WuaCorrelationsComplete = true,
    bool WuaSourcesPartial = false)
{
    public string DisplayLine(bool wuaWasQueried) =>
        $"{Vendor}: " +
        (InventoryComplete
            ? $"{Devices} dispositivi identificati"
            : $"almeno {Devices} dispositivi identificati (inventario parziale)") +
        ", " +
        (!wuaWasQueried
            ? "ricerca Windows Update non verificata; "
            : !WuaCorrelationsComplete
                ? "correlazioni WUA non valutabili (limite di sicurezza); "
                : WuaSourcesPartial
                    ? DevicesWithWuaMatches == 0
                        ? "nessun match nelle sole fonti WUA completate; " +
                          "ricerca PARZIALE, altre fonti non verificate; "
                        : $"almeno {DevicesWithWuaMatches} con candidati Windows Update per ID " +
                          $"({WuaCorrelations} correlazioni dalle sole fonti completate); " +
                          "ricerca PARZIALE, altre fonti non verificate; "
                    : $"{DevicesWithWuaMatches} con candidati Windows Update per ID " +
                      $"({WuaCorrelations} correlazioni, non installazioni approvate); ") +
        $"{DevicesWithoutInstalledVersion} senza versione installata; " +
        "disponibilità dei driver OEM non verificata.";
}

public static class VendorCoverageMatrix
{
    public static IReadOnlyList<VendorCoverageRow> Create(
        IEnumerable<VendorSourceCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        // The home page may mix vendor handoffs and catalog metadata.
        // Metadata is not an additional installed device.
        return checks
            .Where(check => !check.IsCatalogMetadata)
            .GroupBy(check => check.Vendor, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new VendorCoverageRow(
                group.Key,
                group.Count(),
                group.Count(item => item.WuaMatches > 0),
                group.Sum(item => Math.Max(0, item.WuaMatches)),
                group.Count(item => item.InstalledVersion is
                    "non disponibile" or "N/D" or "")))
            .ToArray();
    }
}
