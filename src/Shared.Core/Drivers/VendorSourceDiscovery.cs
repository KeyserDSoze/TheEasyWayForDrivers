using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Hardware-based handoffs to documented official vendor tools/pages.
/// This is NOT a vendor catalog query and a handoff is NEVER an installable
/// driver candidate. Only trusted, fixed HTTPS destinations are emitted.
/// </summary>
public sealed record VendorSourceCheck(
    string DeviceName,
    string Vendor,
    string InstalledVersion,
    string OfficialUrl,
    string SourceName,
    string Evidence,
    int WuaMatches,
    bool IsCatalogMetadata = false,
    string? PublishedVersion = null)
{
    public string Title => DeviceName;

    public string StatusText => IsCatalogMetadata
        ? "Metadati pubblicati dal catalogo OEM: compatibilità con i singoli dispositivi non verificata."
        : WuaMatches > 0
        ? $"{WuaMatches} candidati Windows Update per ID; verifica anche il produttore."
        : "Catalogo produttore da verificare; nessun aggiornamento OEM confermato.";

    public string Detail => IsCatalogMetadata
        ? $"Versione pacchetto: {PublishedVersion ?? "N/D"} · {SourceName} · {Evidence}"
        : $"Installato: {InstalledVersion} · {SourceName} · {Evidence}";
}

public static class VendorSourceDiscovery
{
    public const string IntelDsaUrl =
        "https://www.intel.com/content/www/us/en/support/detect.html";
    public const string IntelWifiUrl =
        "https://www.intel.com/content/www/us/en/download/19351/intel-wireless-wi-fi-drivers-for-windows-10-and-windows-11.html";
    public const string IntelBluetoothUrl =
        "https://www.intel.com/content/www/us/en/download/18649/intel-wireless-bluetooth-drivers-for-windows-10-and-windows-11.html";
    public const string NvidiaDriversUrl =
        "https://www.nvidia.com/Download/Find.aspx";
    public const string AmdDriversUrl =
        "https://www.amd.com/en/support/download/drivers.html";

    public static IReadOnlyList<VendorSourceCheck> Find(
        IEnumerable<DriverInfo> installed,
        IEnumerable<DriverUpdateInfo> wuaUpdates,
        int maxResults = 30)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(wuaUpdates);

        var updates = wuaUpdates.ToArray();
        return installed
            .Where(driver => !string.IsNullOrWhiteSpace(driver.Name))
            .Select(driver => (driver, source: ChooseSource(driver)))
            .Where(pair => pair.source is not null)
            .DistinctBy(pair => pair.driver.DeviceId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(pair => pair.driver.NeedsAttention)
            .ThenBy(pair => pair.source!.Value.Vendor, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.driver.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxResults, 0, 60))
            .Select(pair =>
            {
                var source = pair.source!.Value;
                var matching = DriverUpdateMatcher.FindMatches(pair.driver, updates).Count;
                var evidence = pair.driver.HardwareIds.Count > 0
                    ? "Dispositivo con Hardware ID rilevato; compatibilità OEM non verificata"
                    : "Hardware ID assente; controlla manualmente il modello esatto";
                return new VendorSourceCheck(
                    pair.driver.Name.Trim(),
                    source.Vendor,
                    string.IsNullOrWhiteSpace(pair.driver.DriverVersion)
                        ? "non disponibile" : pair.driver.DriverVersion.Trim(),
                    source.Url,
                    source.Name,
                    evidence,
                    matching);
            })
            .ToArray();
    }

    public static bool IsTrustedOfficialUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            parsed.Port != 443)
            return false;

        return parsed.Host is "www.intel.com" or "www.nvidia.com" or
            "www.amd.com" or "www.dell.com" or "support.hp.com";
    }

    private static (string Vendor, string Name, string Url)? ChooseSource(DriverInfo driver)
    {
        // Distinguish device ownership from a generic vendor display name.
        // Product-family names and PCI vendor IDs are not alone proof of package applicability.
        var label = driver.Name;
        var intel = ContainsVendor(driver, "Intel", "VEN_8086");
        var nvidia = ContainsVendor(driver, "NVIDIA", "VEN_10DE");
        var amd = ContainsVendor(driver, "AMD", "VEN_1002");

        if (label.Contains("Killer", StringComparison.OrdinalIgnoreCase))
        {
            if (IsBluetooth(label))
                return ("Killer / Intel", "Intel Bluetooth · supporto ufficiale", IntelBluetoothUrl);
            if (IsWireless(label))
                return ("Killer / Intel", "Intel Wi-Fi / Killer · supporto ufficiale", IntelWifiUrl);
            return ("Killer / Intel", "Intel Driver & Support Assistant", IntelDsaUrl);
        }

        if (nvidia && IsGraphic(label))
            return ("NVIDIA", "Ricerca driver NVIDIA ufficiale", NvidiaDriversUrl);
        if (nvidia && (label.Contains("Audio", StringComparison.OrdinalIgnoreCase) ||
                       label.Contains("Platform", StringComparison.OrdinalIgnoreCase)))
            return ("NVIDIA", "Driver NVIDIA · pacchetti componenti", NvidiaDriversUrl);

        if (intel && IsBluetooth(label))
            return ("Intel", "Intel Bluetooth · supporto ufficiale", IntelBluetoothUrl);
        if (intel && IsWireless(label))
            return ("Intel", "Intel Wi-Fi · supporto ufficiale", IntelWifiUrl);
        if (intel && IsGraphic(label))
            return ("Intel", "Intel Driver & Support Assistant", IntelDsaUrl);

        if (amd && (IsGraphic(label) || label.Contains("Chipset", StringComparison.OrdinalIgnoreCase)))
            return ("AMD", "Supporto driver AMD ufficiale", AmdDriversUrl);

        return null;
    }

    private static bool ContainsVendor(DriverInfo driver, string brand, string pciVendorId) =>
        driver.Name.Contains(brand, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(driver.Manufacturer, brand, StringComparison.OrdinalIgnoreCase) ||
        driver.HardwareIds.Any(id => id.Contains(pciVendorId, StringComparison.OrdinalIgnoreCase));

    private static bool IsGraphic(string name) =>
        name.Contains("Graphics", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Display", StringComparison.OrdinalIgnoreCase);

    private static bool IsBluetooth(string name) =>
        name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);

    private static bool IsWireless(string name) =>
        name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("WiFi", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("PROSet", StringComparison.OrdinalIgnoreCase);
}
