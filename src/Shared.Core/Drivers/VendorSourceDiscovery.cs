using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

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
    string? PublishedVersion = null,
    bool IsVendorApplicable = false,
    bool IsReleaseAnnouncement = false)
{
    public string Title => DeviceName;

    public string StatusText => IsReleaseAnnouncement
        ? "Annuncio GeForce pubblicato da NVIDIA: NON è un driver verificato per questo PC."
        : IsVendorApplicable
        ? "Segnalato come necessario dal modulo ufficiale del produttore: non installabile tramite OmegaDrive."
        : IsCatalogMetadata
        ? "Metadati pubblicati dal catalogo OEM: compatibilità con i singoli dispositivi non verificata."
        : WuaMatches > 0
        ? $"{WuaMatches} candidati Windows Update per ID; verifica anche il produttore."
        : "Catalogo produttore da verificare; nessun aggiornamento OEM confermato.";

    public string Detail => IsReleaseAnnouncement
        ? "Notizia ufficiale GeForce, non catalogo driver compatibili. " +
          "Versione/installabilità per questa GPU NON verificate; " + Evidence
        : IsVendorApplicable &&
        !string.IsNullOrWhiteSpace(InstalledVersion) &&
        !InstalledVersion.Equals("Non confrontata", StringComparison.OrdinalIgnoreCase)
        ? $"Versione installata segnalata: {InstalledVersion} · " +
          $"Pacchetto consigliato: {PublishedVersion ?? "N/D"} · {SourceName} · {Evidence}"
        : IsCatalogMetadata
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

        var limit = Math.Clamp(maxResults, 0, 150);
        if (limit == 0)
            return [];

        // Multiple WUA passes can return the same update ID. One update must
        // count only once when correlating a device with the catalog.
        var updates = WuaDiagnosticAssociations.Create(wuaUpdates);

        var groups = installed
            .Where(driver => !string.IsNullOrWhiteSpace(driver.Name))
            .Select(driver => (Driver: driver, Source: ChooseSource(driver)))
            .Where(pair => pair.Source is not null)
            .DistinctBy(pair => pair.Driver.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(pair =>
            {
                var source = pair.Source!.Value;
                var matches = updates.Count(update =>
                    update.Matches(pair.Driver));
                return new SourceCandidate(pair.Driver,
                    source.Vendor, source.Name, source.Url, matches);
            })
            .GroupBy(candidate => candidate.Vendor, StringComparer.OrdinalIgnoreCase)
            // If fewer slots than vendors exist, prefer groups with real WUA
            // correlations and device problems rather than alphabetical order.
            .OrderByDescending(group => group.Any(row => row.WuaMatches > 0))
            .ThenByDescending(group => group.Any(row => row.Driver.NeedsAttention))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(row => row.WuaMatches > 0)
                .ThenByDescending(row => row.Driver.NeedsAttention)
                .ThenBy(row => row.Driver.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray())
            .ToArray();

        // Before this change Take(100) followed a vendor sort: systems with
        // many Intel devices could hide NVIDIA/Realtek/Qualcomm completely.
        // Round-robin guarantees diversity within the same bounded budget.
        var selected = new List<SourceCandidate>(limit);
        for (var depth = 0; selected.Count < limit; depth++)
        {
            var any = false;
            foreach (var group in groups)
            {
                if (depth >= group.Length)
                    continue;
                any = true;
                selected.Add(group[depth]);
                if (selected.Count == limit)
                    break;
            }

            if (!any)
                break;
        }

        return selected.Select(candidate =>
        {
            var driver = candidate.Driver;
            var evidence = driver.HardwareIds.Count > 0
                ? "Dispositivo con Hardware ID rilevato; compatibilità OEM non verificata"
                : "Hardware ID assente; controlla manualmente il modello esatto";
            return new VendorSourceCheck(
                driver.Name.Trim(),
                candidate.Vendor,
                string.IsNullOrWhiteSpace(driver.DriverVersion)
                    ? "non disponibile" : driver.DriverVersion.Trim(),
                candidate.Url,
                candidate.SourceName,
                evidence,
                candidate.WuaMatches);
        }).ToArray();
    }

    private sealed record SourceCandidate(
        DriverInfo Driver, string Vendor, string SourceName,
        string Url, int WuaMatches);

    /// <summary>
    /// The same recognized support vendor used in the OEM handoff panel.
    /// This is a local device-class/PNP classification, NOT a queried
    /// catalog result or proof of a compatible update.
    /// </summary>
    public static string? GetRecognizedSupportVendor(DriverInfo driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        return ChooseSource(driver)?.Vendor;
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
            "www.qualcomm.com" or "www.mediatek.com" or
            "support.lenovo.com";
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

        // WMI friendly names and installed-provider strings can be stale
        // or simply wrong. A strict, conflicting PCI Hardware VEN+DEV
        // overrides brand hints for ALL three GPU/vendor branches, not
        // only the Killer-specific routing (see #292).
        var nvidia = (NvidiaHardwareDetector.IsNvidiaDevice(driver) ||
            HasBrand(driver, "NVIDIA")) &&
            !HasForeignPciVendor(driver, "10DE");
        var intel = (IntelHardwareDetector.IsIntelDevice(driver) ||
            HasBrand(driver, "Intel")) &&
            !HasForeignPciVendor(driver, "8086");
        var amd = (AmdHardwareDetector.IsAmdDevice(driver) ||
            HasBrand(driver, "AMD")) &&
            !HasForeignPciVendor(driver, "1002");
        var network = IsNetworkOrBluetooth(driver);
        var audio = IsAudio(driver);
        // Windows can expose DCH extension nodes with only the generic
        // "Software Component" label and Microsoft provider. Use the
        // validated PnP SWC vendor+model identity, not the friendly name.
        var intelSwc = SwcVendorComponentIdentity.IsIntel(driver);
        var realtekSwc = SwcVendorComponentIdentity.IsRealtek(driver);

        var killerAx1675i = KillerWifiHardwareDetector.IsKnownAx1675i(driver);
        if ((label.Contains("Killer", StringComparison.OrdinalIgnoreCase) ||
             killerAx1675i) &&
            !HasForeignPciVendor(driver, "8086"))
        {
            if (IsBluetooth(label) || IsClass(driver, "Bluetooth"))
                return ("Killer / Intel", "Intel Bluetooth · supporto ufficiale", IntelBluetoothUrl);
            if (killerAx1675i || IsWireless(label))
                return ("Killer / Intel", "Intel Wi-Fi / Killer · supporto ufficiale", IntelWifiUrl);
            return ("Killer / Intel", "Intel Driver & Support Assistant", IntelDsaUrl);
        }

        if (nvidia)
            return ("NVIDIA",
                IsGraphic(label) || IsClass(driver, "Display") ||
                PciClassIdentity.HasDisplayClass(driver, "10DE")
                    ? "Ricerca driver GPU NVIDIA ufficiale"
                    : audio || label.Contains("Platform", StringComparison.OrdinalIgnoreCase)
                        ? "NVIDIA · verifica driver audio/componenti"
                        : "NVIDIA · verifica driver del dispositivo",
                NvidiaDriversUrl);

        if (intel && (IsBluetooth(label) || IsClass(driver, "Bluetooth")))
            return ("Intel", "Intel Bluetooth · supporto ufficiale", IntelBluetoothUrl);
        if (intel && IsWireless(label))
            return ("Intel", "Intel Wi-Fi · supporto ufficiale", IntelWifiUrl);
        if (intel && (IsGraphic(label) || IsClass(driver, "Display") ||
                      network || intelSwc ||
                      PciClassIdentity.HasDisplayClass(driver, "8086") ||
                      PciClassIdentity.HasNetworkClass(driver, "8086")))
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
        // Intel SST audio endpoints still use Realtek's codec vendor ID.
        // An uninstalled driver can leave the device class/name generic,
        // but the specific FUNC_01 + VEN_10EC prefix is meaningful.
        var realtekCodecById = HasHardwarePrefix(driver,
            @"HDAUDIO\FUNC_01&VEN_10EC", @"INTELAUDIO\FUNC_01&VEN_10EC");
        var realtek = realtekSwc || HasBrandOrHardwareId(driver, "Realtek",
            @"PCI\VEN_10EC", @"USB\VID_0BDA",
            @"HDAUDIO\FUNC_01&VEN_10EC", @"INTELAUDIO\FUNC_01&VEN_10EC");
        if (realtek && (network || audio || realtekCodecById || realtekSwc ||
                        PciClassIdentity.HasNetworkClass(driver, "10EC")))
            return ("Realtek", "Realtek · ricerca manuale (verifica OEM PC)",
                RealtekSupportUrl);

        var broadcom = HasBrandOrHardwareId(driver, "Broadcom",
            @"PCI\VEN_14E4", @"USB\VID_0A5C");
        if (broadcom &&
            (network || PciClassIdentity.HasNetworkClass(driver, "14E4")))
            return ("Broadcom", "Broadcom · supporto driver (verifica OEM PC)",
                BroadcomSupportUrl);

        var qualcomm = HasBrandOrHardwareId(driver, "Qualcomm",
            @"PCI\VEN_17CB", @"PCI\VEN_168C",
            @"USB\VID_05C6", @"USB\VID_0CF3") ||
            HasBrand(driver, "Atheros");
        if (qualcomm && (network ||
            PciClassIdentity.HasNetworkClass(driver, "168C") ||
            PciClassIdentity.HasNetworkClass(driver, "17CB")))
            return ("Qualcomm / Atheros", "Qualcomm · driver tramite produttore PC",
                QualcommSupportUrl);

        var mediatek = HasBrandOrHardwareId(driver, "MediaTek",
            @"PCI\VEN_14C3", @"USB\VID_0E8D") ||
            HasBrand(driver, "Ralink");
        if (mediatek &&
            (network || PciClassIdentity.HasNetworkClass(driver, "14C3")))
            return ("MediaTek", "MediaTek · consultare il produttore del PC",
                MediaTekSupportUrl);

        return null;
    }

    /// <summary>
    /// A marketing name/provider can disagree with the device's actual
    /// PCI VEN+DEV: Killer, NVIDIA, Intel and AMD handoffs must never
    /// overwrite a conflicting strict PnP identity. Prefer HardwareIds
    /// and consult CompatibleIds only when no valid hardware PCI vendor
    /// exists. Never read DeviceId or infer an installable update.
    /// </summary>
    internal static bool HasForeignPciVendor(DriverInfo device, string expected)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (expected.Length != 4 || !expected.All(Uri.IsHexDigit))
            throw new ArgumentException("Vendor PCI non valido.", nameof(expected));

        static string? VendorOf(string? id)
        {
            const string prefix = @"PCI\VEN_";
            if (!PnpIdentifierFallback.IsStrictSingleId(id) ||
                !id!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                id.Length < prefix.Length + 13 ||
                !id.AsSpan(prefix.Length, 4).ToString().All(Uri.IsHexDigit) ||
                !id.AsSpan(prefix.Length + 4, 5)
                    .Equals("&DEV_", StringComparison.OrdinalIgnoreCase) ||
                !id.AsSpan(prefix.Length + 9, 4).ToString().All(Uri.IsHexDigit) ||
                (id.Length != prefix.Length + 13 &&
                 id[prefix.Length + 13] != '&'))
                return null;
            return id.Substring(prefix.Length, 4);
        }

        var hardware = device.HardwareIds
            .Select(VendorOf).Where(v => v is not null).ToArray();
        var observed = hardware.Length > 0
            ? hardware
            : device.CompatibleIds.Select(VendorOf)
                .Where(v => v is not null).ToArray();
        return observed.Any(v =>
            !string.Equals(v, expected, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasBrand(DriverInfo driver, string brand) =>
        driver.Name.Contains(brand, StringComparison.OrdinalIgnoreCase) ||
        (driver.Manufacturer?.Contains(brand, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (driver.DriverProvider?.Contains(brand, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool HasBrandOrHardwareId(
        DriverInfo driver, string brand, params string[] prefixes) =>
        HasBrand(driver, brand) || HasHardwarePrefix(driver, prefixes);

    // Matching a complete bus/vendor token avoids misclassifying near misses
    // (VID_0CF30 is NOT VID_0CF3; VEN_10EC0 is NOT VEN_10EC).
    private static bool HasHardwarePrefix(
        DriverInfo driver, params string[] prefixes) =>
        driver.HardwareIds.Concat(driver.CompatibleIds).Any(id =>
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;
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
