namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Keeps previously obtained, explicit OEM evidence when ONLY the WUA
/// search is repeated. The manual PnP support preview is regenerated with
/// fresh WUA matches. Full PC scans clear the Desktop's collection first,
/// so no evidence from an old hardware inventory is carried across scans.
/// This is display retention, never authorization to install OEM packages.
/// </summary>
public static class VendorSourceResultRetention
{
    public const int MaximumDisplayedRows = 512;

    public static IReadOnlyList<VendorSourceCheck> KeepEvidence(
        IEnumerable<VendorSourceCheck> displayedRows)
    {
        ArgumentNullException.ThrowIfNull(displayedRows);

        // The source is an in-memory, bounded UI collection. Limit work
        // before deduplication; never let an old IPC/snapshot provide an
        // unbounded list of OEM findings.
        var rows = displayedRows.Take(MaximumDisplayedRows + 1).ToArray();
        if (rows.Length > MaximumDisplayedRows)
            throw new InvalidDataException(
                "Troppe righe OEM per conservare risultati precedenti.");

        return rows
            .Where(row => row is not null &&
                row.GroupedManualDevices == 0 &&
                // Preserve ONLY query/announcement evidence, not manual
                // links that depend on the NEW WUA association count.
                (row.IsCatalogMetadata || row.IsVendorApplicable ||
                 row.IsReleaseAnnouncement) &&
                // Revalidate opening the official support/article URL.
                // An arbitrary legacy row must not survive a refresh.
                VendorSourceDiscovery.IsTrustedOfficialUrl(row.OfficialUrl))
            .Distinct()
            .ToArray();
    }
}
