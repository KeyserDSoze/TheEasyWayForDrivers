namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Short, read-only Desktop summary of official MANUAL support links.
/// VendorSourceDiscovery.Find still yields up to 150 individual PnP-based
/// referrals for device-level QA; only the main results panel collapses
/// them. Never aggregate manufacturer-assessed applicable OEM packages,
/// metadata catalog entries or release announcements.
/// </summary>
public static class VendorManualHandoffSummary
{
    public const int MaximumInputRows = 150;

    private sealed record OfficialSource(
        string DisplayName, IReadOnlyList<string> AllowedVendors);

    // Fixed destinations already used by the trusted local PnP selector.
    // Never pass through arbitrary URLs, query strings, paths, marketing
    // titles, installed versions or per-device identifiers in the summary.
    private static readonly Dictionary<string, OfficialSource> Official =
        new(StringComparer.Ordinal)
        {
            [VendorSourceDiscovery.IntelDsaUrl] =
                new("Intel Driver & Support Assistant",
                    ["Intel", "Killer / Intel"]),
            [VendorSourceDiscovery.IntelWifiUrl] =
                new("Intel Wi-Fi / Killer",
                    ["Intel", "Killer / Intel"]),
            [VendorSourceDiscovery.IntelBluetoothUrl] =
                new("Intel Bluetooth",
                    ["Intel", "Killer / Intel"]),
            [VendorSourceDiscovery.NvidiaDriversUrl] =
                new("NVIDIA · driver e componenti", ["NVIDIA"]),
            [VendorSourceDiscovery.AmdDriversUrl] =
                new("AMD · supporto ufficiale", ["AMD"]),
            [VendorSourceDiscovery.RealtekSupportUrl] =
                new("Realtek · supporto ufficiale", ["Realtek"]),
            [VendorSourceDiscovery.BroadcomSupportUrl] =
                new("Broadcom · supporto ufficiale", ["Broadcom"]),
            [VendorSourceDiscovery.QualcommSupportUrl] =
                new("Qualcomm · supporto ufficiale", ["Qualcomm / Atheros"]),
            [VendorSourceDiscovery.MediaTekSupportUrl] =
                new("MediaTek · supporto ufficiale", ["MediaTek"]),
            [VendorSourceDiscovery.LogitechSupportUrl] =
                new("Logitech · supporto ufficiale", ["Logitech"]),
            [VendorSourceDiscovery.RazerSupportUrl] =
                new("Razer · supporto ufficiale", ["Razer"])
        };

    /// <summary>
    /// One row per (validated vendor, fixed official URL) with counts
    /// measured only in the provided, already deduplicated PnP preview.
    /// A WUA match on a PnP ID is NOT a confirmed OEM update.
    /// </summary>
    public static IReadOnlyList<VendorSourceCheck> Create(
        IEnumerable<VendorSourceCheck> manualReferrals)
    {
        ArgumentNullException.ThrowIfNull(manualReferrals);
        var rows = manualReferrals.Take(MaximumInputRows + 1).ToArray();
        if (rows.Length > MaximumInputRows)
            throw new InvalidDataException(
                "Troppe fonti OEM manuali per il riepilogo.");

        return rows
            .Where(row => row is not null &&
                !row.IsVendorApplicable && !row.IsCatalogMetadata &&
                !row.IsReleaseAnnouncement &&
                Official.TryGetValue(row.OfficialUrl, out var source) &&
                source.AllowedVendors.Contains(row.Vendor,
                    StringComparer.OrdinalIgnoreCase))
            .GroupBy(row => row.Vendor.ToUpperInvariant() + "\u001F" +
                row.OfficialUrl, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var official = Official[first.OfficialUrl];
                var count = group.Count();
                var withWua = group.Count(row => row.WuaMatches > 0);
                return new VendorSourceCheck(
                    $"{official.DisplayName} · {count} " +
                        (count == 1 ? "dispositivo" : "dispositivi"),
                    first.Vendor,
                    "Non confrontata",
                    first.OfficialUrl,
                    official.DisplayName,
                    "Rimando al supporto ufficiale, solo per verifica manuale",
                    WuaMatches: 0)
                {
                    GroupedManualDevices = count,
                    GroupedManualDevicesWithWuaIds = withWua
                };
            })
            .OrderByDescending(row => row.GroupedManualDevicesWithWuaIds > 0)
            .ThenBy(row => row.Vendor, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
