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
    public const string RealtekSupportUrl = "https://www.realtek.com/Download";
    public const string BroadcomSupportUrl = "https://www.broadcom.com/support/download-search";
    public const string QualcommSupportUrl = "https://www.qualcomm.com/drivers";
    public const string MediaTekSupportUrl = "https://www.mediatek.com/company/contact-us";

    public static IReadOnlyList<VendorSourceCheck> Find(
        IEnumerable<DriverInfo> installed,
        IEnumerable<DriverUpdateInfo> wuaUpdates,
        int maxResults = 100)
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
            .Take(Math.Clamp(maxResults, 0, 150))
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
            "www.amd.com" or "www.dell.com" or "support.hp.com" or
            "www.realtek.com" or "www.broadcom.com" or
            "www.qualcomm.com" or "www.mediatek.com";
    }

    private static (string Vendor, string Name, string Url)? ChooseSource(DriverInfo driver)
    {
        // This only selects a vendor handoff. It never identifies a newer
        // compatible driver, so generic Windows device names are acceptable
        // when vendor identification comes from PnP hardware IDs.
        var label = driver.Name;
        // Infrastructure PCI bridges are not actionable vendor drivers.
        if (label.Contains("Root Port", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Host Bridge", StringComparison.OrdinalIgnoreCase))
            return null;

        var nvidia = NvidiaHardwareDetector.IsNvidiaDevice(driver) ||
            HasBrand(driver, "NVIDIA");
        var intel = IntelHardwareDetector.IsIntelDevice(driver) ||
            HasBrand(driver, "Intel");
        var amd = AmdHardwareDetector.IsAmdDevice(driver) ||
            HasBrand(driver, "AMD");
        var network = IsNetworkOrBluetooth(driver);
        var audio = IsAudio(driver);

        if (label.Contains("Killer", StringComparison.OrdinalIgnoreCase))
        {
            if (IsBluetooth(label) || IsClass(driver, "Bluetooth"))
                return ("Killer / Intel", "Intel Bluetooth · supporto ufficiale", IntelBluetoothUrl);
            if (IsWireless(label))
                return ("Killer / Intel", "Intel Wi-Fi / Killer · supporto ufficiale", IntelWifiUrl);
            return ("Killer / Intel", "Intel Driver & Support Assistant", IntelDsaUrl);
        }

        if (nvidia)
            return ("NVIDIA",
                IsGraphic(label) || IsClass(driver, "Display")
                    ? "Ricerca driver GPU NVIDIA ufficiale"
                    : audio || label.Contains("Platform", StringComparison.OrdinalIgnoreCase)
                        ? "NVIDIA · verifica driver audio/componenti"
                        : "NVIDIA · verifica driver del dispositivo",
                NvidiaDriversUrl);

        if (intel && (IsBluetooth(label) || IsClass(driver, "Bluetooth")))
            return ("Intel", "Intel Bluetooth · supporto ufficiale", IntelBluetoothUrl);
        if (intel && IsWireless(label))
            return ("Intel", "Intel Wi-Fi · supporto ufficiale", IntelWifiUrl);
        if (intel && (IsGraphic(label) || IsClass(driver, "Display") || network))
            return ("Intel", "Intel Driver & Support Assistant", IntelDsaUrl);

        if (amd && (IsGraphic(label) || IsClass(driver, "Display") ||
                    label.Contains("Chipset", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("SMBus", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("PSP", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("GPIO", StringComparison.OrdinalIgnoreCase)))
            return ("AMD", "AMD · ricerca driver ufficiale", AmdDriversUrl);

        // For laptop-specific audio, network and Bluetooth packages the
        // system/motherboard OEM may be preferable to the generic vendor site.
        // The following links offer manual checks, NOT verified downloads.
        var realtek = HasBrandOrHardwareId(driver, "Realtek",
            @"PCI\VEN_10EC", @"USB\VID_0BDA", @"HDAUDIO\FUNC_01&VEN_10EC");
        if (realtek && (network || audio))
            return ("Realtek", "Realtek · ricerca manuale (verifica OEM PC)",
                RealtekSupportUrl);

        var broadcom = HasBrandOrHardwareId(driver, "Broadcom",
            @"PCI\VEN_14E4", @"USB\VID_0A5C");
        if (broadcom && network)
            return ("Broadcom", "Broadcom · supporto driver (verifica OEM PC)",
                BroadcomSupportUrl);

        var qualcomm = HasBrandOrHardwareId(driver, "Qualcomm",
            @"PCI\VEN_17CB", @"PCI\VEN_168C", @"USB\VID_05C6") ||
            HasBrand(driver, "Atheros");
        if (qualcomm && network)
            return ("Qualcomm / Atheros", "Qualcomm · driver tramite produttore PC",
                QualcommSupportUrl);

        var mediatek = HasBrandOrHardwareId(driver, "MediaTek",
            @"PCI\VEN_14C3", @"USB\VID_0E8D") ||
            HasBrand(driver, "Ralink");
        if (mediatek && network)
            return ("MediaTek", "MediaTek · consultare il produttore del PC",
                MediaTekSupportUrl);

        return null;
    }

    private static bool HasBrand(DriverInfo driver, string brand) =>
        driver.Name.Contains(brand, StringComparison.OrdinalIgnoreCase) ||
        (driver.Manufacturer?.Contains(brand, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (driver.DriverProvider?.Contains(brand, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool HasBrandOrHardwareId(
        DriverInfo driver, string brand, params string[] prefixes) =>
        HasBrand(driver, brand) ||
        driver.HardwareIds.Concat(driver.CompatibleIds).Any(id =>
        {
            var value = id.Trim();
            return prefixes.Any(prefix =>
                value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                (value.Length == prefix.Length ||
                 value[prefix.Length] is '&' or '\\'));
        });

    private static bool IsClass(DriverInfo driver, string name) =>
        string.Equals(driver.DeviceClass, name, StringComparison.OrdinalIgnoreCase);

    private static bool IsAudio(DriverInfo driver) =>
        IsClass(driver, "MEDIA") ||
        driver.Name.Contains("Audio", StringComparison.OrdinalIgnoreCase) ||
        driver.Name.Contains("Sound", StringComparison.OrdinalIgnoreCase);

    private static bool IsNetworkOrBluetooth(DriverInfo driver) =>
        IsClass(driver, "Net") || IsClass(driver, "Bluetooth") ||
        IsBluetooth(driver.Name) || IsWireless(driver.Name) ||
        driver.Name.Contains("Ethernet", StringComparison.OrdinalIgnoreCase) ||
        driver.Name.Contains("Network", StringComparison.OrdinalIgnoreCase);

    private static bool IsGraphic(string name) =>
        name.Contains("Graphics", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Display", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("GPU", StringComparison.OrdinalIgnoreCase);

    private static bool IsBluetooth(string name) =>
        name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);

    private static bool IsWireless(string name) =>
        name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("WiFi", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("PROSet", StringComparison.OrdinalIgnoreCase);
}
