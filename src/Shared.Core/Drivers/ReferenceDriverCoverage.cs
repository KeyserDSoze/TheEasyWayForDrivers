using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Fixed, privacy-safe comparison categories for the eight rows reported in #91.
/// This is diagnostic evidence, NOT a vendor catalog query or an update decision.
/// </summary>
public sealed record ReferenceCoverageRow(
    string Component,
    int Devices,
    int WindowsDriverCandidates,
    int SoftwareTitleHints,
    bool SoftwareOnly,
    int WindowsModelReviewHints = 0,
    bool WuaAssociationsIncomplete = false)
{
    public string DisplayLine(bool inventoryComplete, bool driverSourceQueried,
        bool softwareSourceQueried, bool driverPartial = false,
        bool softwarePartial = false)
    {
        if (SoftwareOnly)
            return $"{Component}: {(softwareSourceQueried
                ? $"{SoftwareTitleHints} titoli WUA Type=Software (solo indizi)" +
                  (softwarePartial ? " · ricerca parziale" : "")
                : "ricerca WUA Type=Software non verificata")}; " +
                "non sono driver confermati.";

        var inventory = inventoryComplete
            ? $"{Devices} dispositivi classificati"
            : "inventario PnP non verificato";
        var drivers = driverSourceQueried
            ? $"{WindowsDriverCandidates} candidati WUA Type=Driver correlati per ID" +
              (driverPartial ? " · ricerca parziale" : "") +
              (inventoryComplete && Devices > 0 && WuaAssociationsIncomplete
                  ? WindowsDriverCandidates == 0
                      ? " · ID WUA incompleti: 0 match osservati, assenza non verificabile"
                      : " · ID WUA incompleti: ulteriori associazioni possibili"
                  : "")
            : "ricerca WUA Type=Driver non verificata";
        var review = driverSourceQueried && WindowsModelReviewHints > 0
            ? $"; {WindowsModelReviewHints} indizi WUA Type=Driver per modello " +
              "generico o stesso sottosistema senza revisione (solo revisione, NON installabili)"
            : string.Empty;
        var software = softwareSourceQueried
            ? $"{SoftwareTitleHints} titoli WUA Type=Software (senza matching PnP)" +
              (softwarePartial ? " · ricerca parziale" : "")
            : "ricerca WUA Type=Software non verificata";
        return $"{Component}: {inventory}; {drivers}{review}; {software}.";
    }
}

public static class ReferenceDriverCoverage
{
    // Labels are compiled-in. Do not print arbitrary device titles, identifiers,
    // driver versions, software titles or local paths in the copied report.
    private static readonly (string Name, bool SoftwareOnly)[] Categories =
    [
        ("Intel · grafica", false),
        ("Intel · Bluetooth", false),
        ("Intel · PROSet / Wi-Fi extension", false),
        ("Killer · Networking Software", true),
        ("Killer · scheda Wi-Fi", false),
        ("NVIDIA · GPU", false),
        ("NVIDIA · HD Audio", false),
        ("NVIDIA · Platform Controllers", false)
    ];

    public static IReadOnlyList<ReferenceCoverageRow> Create(
        IEnumerable<DriverInfo> devices,
        DriverSearchReport? search)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var counts = Enumerable.Range(0, Categories.Length)
            .Select(_ => new Count()).ToArray();
        // Only successfully queried WUA Type=Driver GUIDs belong in the
        // eight-row benchmark. An old/malformed IPC payload may include
        // non-GUID rows or OEM/manual records in Updates: those are NOT
        // confirmed WUA drivers. Use the same trusted-source rule and
        // bounded same-GUID merge as the Home diagnostic association.
        // A search with no successful Type=Driver pass has no confirmed
        // candidates, even if an old snapshot carries stale rows.
        var verifiedByGuid = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);
        WindowsDriverCandidateMerge.Merge(verifiedByGuid,
            WindowsDriverCandidateMerge.ConfirmedForReport(search));
        var updates = verifiedByGuid.Values;
        // The IPC cap can leave one confirmed Type=Driver GUID with more
        // PnP IDs than WUA exposed. A missing exact association on ANY
        // classified device then means "not observed", never "no offer".
        // Unknown/OEM records and unsuccessful WUA passes were excluded
        // from verifiedByGuid above. Do not manufacture a new candidate.
        var anyIncompleteWuaIds = updates.Any(update =>
            update.AdditionalHardwareIdsIncomplete);

        // Index each classified PnP Hardware/Compatible ID once. Before
        // this, every device traversed every WUA GUID and up to 1024 IDs,
        // making the Home's eight-row coverage quadratic on large scans.
        // One ID can belong to multiple PnP categories: preserve ALL
        // correlated category counts using a bit mask, not first match.
        var categoriesByPnpId = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        // The PnP scan sometimes reports only an OEM-specific SUBSYS/REV
        // Hardware ID with NO generic Compatible ID. A confirmed WUA GUID
        // may declare the public model or identical SUBSYS minus REV: show these in
        // a separate REVIEW-ONLY bucket. Index models once per category,
        // not once per update x device x entry. Never infer from a title.
        var publicReviewModels = new Dictionary<string,
            Dictionary<int, DriverInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in PnpDistinctTrustedDevices.Create(devices))
        {
            if (ClassifyDevice(device) is not int category)
                continue;

            counts[category].Devices++;
            var categoryBit = 1 << category;
            foreach (var id in device.HardwareIds.Concat(device.CompatibleIds))
            {
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var normalized = id.Trim();
                categoriesByPnpId[normalized] =
                    categoriesByPnpId.GetValueOrDefault(normalized) | categoryBit;
            }

            foreach (var model in WuaGenericModelCorrelation.ReviewModelKeys(device))
            {
                if (!publicReviewModels.TryGetValue(model, out var categories))
                {
                    categories = new Dictionary<int, DriverInfo>();
                    publicReviewModels.Add(model, categories);
                }
                // For a given public model and category, one representative
                // device suffices. Any exact ID match is tracked separately.
                categories.TryAdd(category, device);
            }
        }

        // Inspect only WUA-declared IDs; never infer an association from a
        // vendor name, model title, driver version or generic SUBSYS prefix.
        // A later WUA pass may declare another device ID for the SAME GUID:
        // both categories are counted, but each GUID is unique per row.
        if (categoriesByPnpId.Count > 0)
        {
            foreach (var update in updates)
            {
                if (string.IsNullOrWhiteSpace(update.Id))
                    continue;

                var mask = 0;
                var reviewCandidates = new Dictionary<int, DriverInfo>();
                void Match(string? id)
                {
                    if (!DriverUpdateMatcher.IsCorrelatableHardwareId(id))
                        return;
                    var key = id!.Trim();
                    if (categoriesByPnpId.TryGetValue(key, out var bit))
                        mask |= bit;
                    if (publicReviewModels.TryGetValue(key, out var categories))
                        foreach (var (category, device) in categories)
                            reviewCandidates.TryAdd(category, device);
                }

                Match(update.HardwareId);
                foreach (var id in (update.AdditionalHardwareIds ?? [])
                    .Take(WuaDriverEntryLimits.MaximumAdditionalIds))
                    Match(id);

                for (var index = 0; index < counts.Length; index++)
                {
                    if ((mask & (1 << index)) != 0)
                        counts[index].DriverIds.Add(update.Id);
                    else if (reviewCandidates.TryGetValue(index, out var device) &&
                        WuaGenericModelCorrelation.IsWeakModelOnlyMatch(
                            device, update))
                        counts[index].ReviewIds.Add(update.Id);
                }
            }
        }

        // A later completed WUA pass may declare an EXACT ID for the same
        // GUID and category. Exact evidence wins, regardless of pass order.
        foreach (var row in counts)
            row.ReviewIds.ExceptWith(row.DriverIds);

        // Type=Software is an *unbound title hint*: no association with a
        // device/Hardware ID and absolutely no installability inference.
        foreach (var hint in WuaDriverRelatedSoftwareClassifier
            .ConfirmedForReport(search))
        {
            var category = ClassifySoftwareTitle(hint.Title);
            if (category is int index)
                counts[index].SoftwareTitles++;
        }

        return Categories.Select((category, index) =>
            new ReferenceCoverageRow(category.Name, counts[index].Devices,
                counts[index].DriverIds.Count, counts[index].SoftwareTitles,
                category.SoftwareOnly, counts[index].ReviewIds.Count,
                WuaAssociationsIncomplete:
                    !category.SoftwareOnly && counts[index].Devices > 0 &&
                    anyIncompleteWuaIds)).ToArray();
    }

    private static int? ClassifyDevice(DriverInfo device)
    {
        var name = device.Name ?? string.Empty;
        var type = device.DeviceClass ?? string.Empty;
        // A stale display name/driver provider can still read "NVIDIA" or
        // "Intel" on a generic Windows device whose validated PnP model is
        // actually Realtek, Qualcomm or another vendor. Cross-bus identity
        // takes precedence over those strings for the eight-row QA matrix.
        // Unknown identities retain historical review behavior; conflicting
        // primary vendors fail closed.
        var knownVendor = PnpKnownVendorIdentity.FromDriver(device);
        bool allows(PnpKnownVendorIdentity.Vendor vendor) =>
            knownVendor is null || knownVendor == vendor;
        bool named(string word) => name.Contains(word, StringComparison.OrdinalIgnoreCase);
        bool isClass(string value) => type.Equals(value, StringComparison.OrdinalIgnoreCase);

        if (NvidiaHardwareDetector.IsNvidiaDevice(device) &&
            allows(PnpKnownVendorIdentity.Vendor.Nvidia) &&
            !VendorSourceDiscovery.HasForeignPciVendor(device, "10DE"))
        {
            if (isClass("Display") || named("GeForce") || named("RTX") ||
                named("Graphics") || named("GPU") ||
                PciClassIdentity.HasDisplayClass(device, "10DE"))
                return 5;
            if (isClass("MEDIA") || named("Audio") ||
                HasHardwarePrefix(device, @"HDAUDIO\"))
                return 6;
            // An NVIDIA SoftwareComponent may be called only "Software
            // Component" or "System Controller" by Windows. Recognize its
            // validated SWC/ACPI bus+model identity without guessing from
            // a similar title or unrelated vendor hardware.
            if (named("Platform") ||
                NvidiaHardwareDetector.HasNvidiaPlatformId(device))
                return 7;
        }

        // Brand requires the documented Killer subsystem, not VEN_8086
        // or DEV_51F1 alone. A generic Windows name is sufficient then.
        if (allows(PnpKnownVendorIdentity.Vendor.Intel) &&
            KillerWifiHardwareDetector.IsKnownAx1675i(device))
            return 4;

        if (IntelHardwareDetector.IsIntelDevice(device) &&
            allows(PnpKnownVendorIdentity.Vendor.Intel) &&
            !VendorSourceDiscovery.HasForeignPciVendor(device, "8086"))
        {
            // Intel Wi-Fi extensions can be WUA Type=Driver, unlike unbound
            // Type=Software titles. Demand a relevant PnP class and explicit
            // Wi-Fi/PROSet extension evidence, never a generic Intel match.
            if ((isClass("Extension") || isClass("SoftwareComponent")) &&
                (isClass("Extension") || named("Extension")) &&
                !named("Bluetooth") && !named("Killer") &&
                (named("PROSet") || named("Wi-Fi") || named("WiFi") ||
                 named("WLAN") || named("Wireless")))
                return 2;
            if (isClass("Display") || named("Graphics") ||
                PciClassIdentity.HasDisplayClass(device, "8086"))
                return 0;
            if (isClass("Bluetooth") || named("Bluetooth"))
                return 1;
        }

        // Killer is a product branding, not a unique Intel vendor ID:
        // PCI\VEN_8086 alone cannot prove that this is a Killer adapter.
        if ((named("Killer") ||
             Contains(device.Manufacturer, "Killer") ||
             Contains(device.DriverProvider, "Killer")) &&
            allows(PnpKnownVendorIdentity.Vendor.Intel) &&
            !VendorSourceDiscovery.HasForeignPciVendor(device, "8086") &&
            (isClass("Net") || named("Wi-Fi") || named("WiFi") ||
             named("Wireless")))
            return 4;

        return null;
    }

    private static int? ClassifySoftwareTitle(string title)
    {
        bool has(string word) => title.Contains(word, StringComparison.OrdinalIgnoreCase);
        if (has("PROSet") || (has("Intel") && has("Extension") &&
            (has("Wi-Fi") || has("WiFi") || has("Wireless"))))
            return 2;
        if (has("Killer") && (has("Networking") || has("Software")))
            return 3;
        if (has("Intel") && has("Bluetooth"))
            return 1;
        if (has("Intel") && (has("Graphics") || has("Display")))
            return 0;
        if (has("Killer") && (has("Wi-Fi") || has("WiFi") || has("Wireless")))
            return 4;
        if (has("NVIDIA"))
        {
            if (has("Platform") || has("Controller"))
                return 7;
            if (has("Audio"))
                return 6;
            if (has("Graphics") || has("GeForce") || has("Display"))
                return 5;
        }

        return null;
    }

    private static bool HasHardwarePrefix(DriverInfo device, string prefix) =>
        device.HardwareIds.Concat(device.CompatibleIds).Any(id =>
            id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool Contains(string? value, string word) =>
        value?.Contains(word, StringComparison.OrdinalIgnoreCase) == true;

    private sealed class Count
    {
        public int Devices { get; set; }
        public HashSet<string> DriverIds { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ReviewIds { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public int SoftwareTitles { get; set; }
    }
}
