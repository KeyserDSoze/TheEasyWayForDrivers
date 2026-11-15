namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Replaces the automatic Dell Driver Pack / HP CMSL metadata after an
/// explicit read-only retry without erasing independent, opt-in DCU / HPIA
/// recommendations. "IsCatalogMetadata" alone is NOT a source identity:
/// companion recommendations carry that flag too.
/// </summary>
public static class AutomaticOemCatalogRowReplacement
{
    private const string DellCatalogSource = "Dell Driver Pack Catalog";
    private const string HpCatalogSource = "HP Client Management Script Library";
    private const int MaximumRefreshedRows = 120;

    public static IReadOnlyList<VendorSourceCheck> ForRetry(
        IEnumerable<VendorSourceCheck> displayedRows,
        string vendor,
        IEnumerable<VendorSourceCheck> refreshedCatalogRows)
    {
        ArgumentNullException.ThrowIfNull(displayedRows);
        ArgumentNullException.ThrowIfNull(refreshedCatalogRows);

        // Only the two automatic metadata probes can be retried from Home.
        // Do not accept arbitrary vendor/source labels from IPC or snapshots.
        var source = vendor switch
        {
            "Dell" => DellCatalogSource,
            "HP" => HpCatalogSource,
            _ => throw new ArgumentException(
                "Unsupported automatic OEM catalog vendor.", nameof(vendor))
        };

        // Validate the replacement before touching any existing evidence.
        // These are bounded UI previews (Dell <=12, HP <=60), not downloads
        // or installation plans. Reject a different producer masquerading as
        // a catalog result rather than hiding/replacing unrelated evidence.
        var refreshed = refreshedCatalogRows
            .Take(MaximumRefreshedRows + 1).ToArray();
        if (refreshed.Length > MaximumRefreshedRows ||
            refreshed.Any(row => !IsAutomaticCatalogRow(row, vendor, source)))
            throw new InvalidDataException(
                "Automatic OEM catalog retry returned unexpected metadata.");

        return displayedRows
            .Where(row => !IsAutomaticCatalogRow(row, vendor, source))
            .Concat(refreshed)
            .ToArray();
    }

    private static bool IsAutomaticCatalogRow(
        VendorSourceCheck? row, string vendor, string source) =>
        row is not null &&
        row.IsCatalogMetadata &&
        !row.IsVendorApplicable &&
        !row.IsReleaseAnnouncement &&
        row.GroupedManualDevices == 0 &&
        string.Equals(row.Vendor, vendor, StringComparison.Ordinal) &&
        string.Equals(row.SourceName, source, StringComparison.Ordinal);
}
