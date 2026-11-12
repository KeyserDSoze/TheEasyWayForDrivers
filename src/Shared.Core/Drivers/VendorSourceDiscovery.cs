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
    // UI-only aggregation of MANUAL support links; neither an offered
    // driver nor an installation authorization. Counts refer to the
    // bounded PnP preview, not every device on the user's machine.
    public int GroupedManualDevices { get; init; }
    public int GroupedManualDevicesWithWuaIds { get; init; }
    public bool IsInstallable => false;

    public string Title => DeviceName;

    public string StatusText => GroupedManualDevices > 0
        ? "Solo supporto manuale, NON aggiornamenti verificati: " +
          $"{GroupedManualDevices} dispositivi nell'anteprima; " +
          $"{GroupedManualDevicesWithWuaIds} con candidato WUA per ID. " +
          "Nessuna disponibilità o installabilità OEM verificata."
        : IsReleaseAnnouncement
        ? "Annuncio GeForce pubblicato da NVIDIA: NON è un driver verificato per questo PC."
        : IsVendorApplicable
        ? "Segnalato come necessario dal modulo ufficiale del produttore: non installabile tramite OmegaDrive."
        : IsCatalogMetadata
        ? "Metadati pubblicati dal catalogo OEM: compatibilità con i singoli dispositivi non verificata."
        : WuaMatches > 0
        ? $"{WuaMatches} candidati Windows Update per ID; verifica anche il produttore."
        : "Catalogo produttore da verificare; nessun aggiornamento OEM confermato.";

    public string Detail => GroupedManualDevices > 0
        ? "Sintesi delle fonti ufficiali per dispositivo: verifica " +
          "manuale di modello, versione, firma e compatibilità sul sito. " +
          "Non è un elenco di aggiornamenti scaricabili."
        : IsReleaseAnnouncement
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
    public const string LogitechSupportUrl = "https://support.logi.com/";
    public const string RazerSupportUrl = "https://mysupport.razer.com/";

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

        // Validate ALL rows of an instance before source/name filtering:
        // a conflicting secondary Windows row must veto an OEM handoff,
        // even if its display name is empty or unhelpful.
        var groups = PnpDistinctTrustedDevices.Create(installed)
            .Where(driver => !string.IsNullOrWhiteSpace(driver.Name))
            .Select(driver => (Driver: driver, Source: ChooseSource(driver)))
            .Where(pair => pair.Source is not null)
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
            "support.lenovo.com" or "support.logi.com" or
            "mysupport.razer.com";
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

        // WMI friendly names and installed providers can be stale or
        // wrong. The validated *primary* PnP bus+model identity outranks
        // these marketing strings on USB, HDAUDIO, INTELAUDIO, DCH SWC
        // and ACPI as well as PCI. A conflicting pair of recognized
        // hardware vendors fails closed rather than inventing a source.
        var modelVendor = PnpKnownVendorIdentity.FromDriver(driver);
        bool Allows(PnpKnownVendorIdentity.Vendor vendor) =>
            modelVendor is null || modelVendor == vendor;

        // Unlike a brand in a device name, a strict USB/HID VID+PID
        // belongs to a concrete peripheral product. This is a manual
        // support-page handoff only; an unrelated PCI/SWC/ACPI device with
        // a misleading compatible ID must not inherit a USB OEM vendor.
        // Never infer Logitech/Razer from WMI manufacturer/provider text.
        if ((modelVendor is PnpKnownVendorIdentity.Vendor.Logitech or
             PnpKnownVendorIdentity.Vendor.Razer) &&
            HasUsbHidPeripheralContext(driver))
            return modelVendor == PnpKnownVendorIdentity.Vendor.Logitech
                ? ("Logitech", "Logitech · supporto ufficiale (verifica manuale)",
                    LogitechSupportUrl)
                : ("Razer", "Razer · supporto ufficiale (verifica manuale)",
                    RazerSupportUrl);

        var nvidia = (NvidiaHardwareDetector.IsNvidiaDevice(driver) ||
            HasBrand(driver, "NVIDIA")) &&
            Allows(PnpKnownVendorIdentity.Vendor.Nvidia) &&
            !HasForeignPciVendor(driver, "10DE");
        var intel = (IntelHardwareDetector.IsIntelDevice(driver) ||
            HasBrand(driver, "Intel")) &&
            Allows(PnpKnownVendorIdentity.Vendor.Intel) &&
            !HasForeignPciVendor(driver, "8086", "1A56");
        var amd = (AmdHardwareDetector.IsAmdDevice(driver) ||
            HasBrand(driver, "AMD")) &&
            Allows(PnpKnownVendorIdentity.Vendor.Amd) &&
            !HasForeignPciVendor(driver, "1002", "1022");
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
            Allows(PnpKnownVendorIdentity.Vendor.Intel) &&
            !HasForeignPciVendor(driver, "8086", "1A56"))
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
        // A complete Realtek SoundWire codec model identifies the vendor
        // even when Windows calls the device just "Generic audio codec".
        // Do not promote MAN-only hints or generic bus aggregators.
        var realtekSoundWireByModel = driver.HardwareIds
            .Concat(driver.CompatibleIds)
            .Any(id => (id.StartsWith(@"SNDW\", StringComparison.OrdinalIgnoreCase) ||
                        id.StartsWith(@"SOUNDWIRE\", StringComparison.OrdinalIgnoreCase)) &&
                PnpKnownVendorIdentity.FromId(id) ==
                    PnpKnownVendorIdentity.Vendor.Realtek);
        var realtek = (realtekSwc || realtekSoundWireByModel ||
            HasBrandOrHardwareId(driver, "Realtek",
            @"PCI\VEN_10EC", @"USB\VID_0BDA",
            @"HDAUDIO\FUNC_01&VEN_10EC", @"INTELAUDIO\FUNC_01&VEN_10EC")) &&
            Allows(PnpKnownVendorIdentity.Vendor.Realtek) &&
            !HasForeignPciVendor(driver, "10EC");
        if (realtek && (network || audio || realtekCodecById || realtekSwc ||
                        realtekSoundWireByModel ||
                        PciClassIdentity.HasNetworkClass(driver, "10EC")))
            return ("Realtek", "Realtek · ricerca manuale (verifica OEM PC)",
                RealtekSupportUrl);

        var broadcom = HasBrandOrHardwareId(driver, "Broadcom",
            @"PCI\VEN_14E4", @"USB\VID_0A5C") &&
            Allows(PnpKnownVendorIdentity.Vendor.Broadcom) &&
            !HasForeignPciVendor(driver, "14E4");
        if (broadcom &&
            (network || PciClassIdentity.HasNetworkClass(driver, "14E4")))
            return ("Broadcom", "Broadcom · supporto driver (verifica OEM PC)",
                BroadcomSupportUrl);

        var qualcomm = (HasBrandOrHardwareId(driver, "Qualcomm",
            @"PCI\VEN_17CB", @"PCI\VEN_168C",
            @"USB\VID_05C6", @"USB\VID_0CF3") ||
            HasBrand(driver, "Atheros")) &&
            Allows(PnpKnownVendorIdentity.Vendor.Qualcomm) &&
            !HasForeignPciVendor(driver, "17CB", "168C");
        if (qualcomm && (network ||
            PciClassIdentity.HasNetworkClass(driver, "168C") ||
            PciClassIdentity.HasNetworkClass(driver, "17CB")))
            return ("Qualcomm / Atheros", "Qualcomm · driver tramite produttore PC",
                QualcommSupportUrl);

        var mediatek = (HasBrandOrHardwareId(driver, "MediaTek",
            @"PCI\VEN_14C3", @"USB\VID_0E8D") ||
            HasBrand(driver, "Ralink")) &&
            Allows(PnpKnownVendorIdentity.Vendor.MediaTek) &&
            !HasForeignPciVendor(driver, "14C3");
        if (mediatek &&
            (network || PciClassIdentity.HasNetworkClass(driver, "14C3")))
            return ("MediaTek", "MediaTek · consultare il produttore del PC",
                MediaTekSupportUrl);

        return null;
    }

    /// <summary>
    /// A validated PCI VEN+DEV identity takes precedence over stale brand
    /// names for all chip vendors, including network vendors with multiple
    /// known PCI IDs. HardwareIds outrank CompatibleIds; malformed or
    /// incomplete PCI strings are not treated as authoritative evidence.
    /// This only selects a read-only support source, never an update.
    /// </summary>
    internal static bool HasForeignPciVendor(DriverInfo device, params string[] expected)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (expected.Length == 0 ||
            expected.Any(v => v.Length != 4 || !v.All(Uri.IsHexDigit)))
            throw new ArgumentException("Vendor PCI non valido.", nameof(expected));

        var hardware = device.HardwareIds
            .Select(GetStrictPciVendor).Where(v => v is not null).ToArray();
        var observed = hardware.Length > 0
            ? hardware
            : device.CompatibleIds.Select(GetStrictPciVendor)
                .Where(v => v is not null).ToArray();
        return observed.Any(v => !expected.Any(code =>
            string.Equals(code, v, StringComparison.OrdinalIgnoreCase)));
    }

    private static string? GetStrictPciVendor(string? id)
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

    private static bool HasBrand(DriverInfo driver, string brand) =>
        driver.Name.Contains(brand, StringComparison.OrdinalIgnoreCase) ||
        (driver.Manufacturer?.Contains(brand, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (driver.DriverProvider?.Contains(brand, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool HasBrandOrHardwareId(
        DriverInfo driver, string brand, params string[] prefixes) =>
        HasBrand(driver, brand) || HasHardwarePrefix(driver, prefixes);

    // An OEM manual handoff must not promote a generic USB VID,
    // audio FUNC+VEN, or an invalid PCI DEV suffix into a known hardware
    // manufacturer. Use the SAME full, public model grammar as the
    // Windows Update Catalog handoff, which rejects incomplete tokens,
    // fake fields and private PnP instance paths. This classification
    // still NEVER proves that a vendor driver is available or installable.
    private static bool HasHardwarePrefix(
        DriverInfo driver, params string[] prefixes) =>
        driver.HardwareIds.Concat(driver.CompatibleIds).Any(id =>
        {
            var model = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id);
            if (model is null)
                return false;

            return prefixes.Any(prefix =>
                prefix.StartsWith(@"PCI\VEN_", StringComparison.OrdinalIgnoreCase)
                    ? model.StartsWith(prefix + "&DEV_",
                        StringComparison.OrdinalIgnoreCase)
                    : model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                      model.Length > prefix.Length &&
                      model[prefix.Length] == '&');
        });

    /// <summary>
    /// A weak CompatibleID from a USB/HID mouse/keyboard is useful for a
    /// manual handoff only when the PnP node is a peripheral. PCI, ACPI,
    /// audio and software components cannot be relabelled as a USB product
    /// through a stale compatible ID, even when it contains a valid VID/PID.
    /// </summary>
    private static bool HasUsbHidPeripheralContext(DriverInfo device)
    {
        static bool IsPeripheralBus(string id) =>
            id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase);

        var primary = device.HardwareIds
            .Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        if (primary.Any(id => !IsPeripheralBus(id) &&
            !id.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase) &&
            !id.StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (primary.Any(IsPeripheralBus))
            return true;

        return (IsClass(device, "HIDClass") ||
                IsClass(device, "Mouse") ||
                IsClass(device, "Keyboard") ||
                IsClass(device, "USB")) &&
            device.CompatibleIds.Any(IsPeripheralBus);
    }

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
