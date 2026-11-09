using TheEasyWayForDrivers.Core.Models;

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
    bool SoftwareOnly)
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
              (driverPartial ? " · ricerca parziale" : "")
            : "ricerca WUA Type=Driver non verificata";
        var software = softwareSourceQueried
            ? $"{SoftwareTitleHints} titoli WUA Type=Software (senza matching PnP)" +
              (softwarePartial ? " · ricerca parziale" : "")
            : "ricerca WUA Type=Software non verificata";
        return $"{Component}: {inventory}; {drivers}; {software}.";
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
        ("Intel · PROSet / Wi-Fi extension", true),
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
        var updates = search?.Updates ?? [];

        // A single PnP instance can occur more than once in upstream views.
        foreach (var device in devices
            .Where(device => !string.IsNullOrWhiteSpace(device.DeviceId))
            .DistinctBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase))
        {
            var category = ClassifyDevice(device);
            if (category is not int index)
                continue;

            var count = counts[index];
            count.Devices++;
            foreach (var update in updates)
            {
                if (DriverUpdateMatcher.IsMatch(device, update) &&
                    !string.IsNullOrWhiteSpace(update.Id))
                    count.DriverIds.Add(update.Id);
            }
        }

        // Type=Software is an *unbound title hint*: no association with a
        // device/Hardware ID and absolutely no installability inference.
        foreach (var hint in (search?.SoftwareReviewItems ?? [])
            .DistinctBy(hint => hint.UpdateId, StringComparer.OrdinalIgnoreCase))
        {
            var category = ClassifySoftwareTitle(hint.Title);
            if (category is int index)
                counts[index].SoftwareTitles++;
        }

        return Categories.Select((category, index) =>
            new ReferenceCoverageRow(category.Name, counts[index].Devices,
                counts[index].DriverIds.Count, counts[index].SoftwareTitles,
                category.SoftwareOnly)).ToArray();
    }

    private static int? ClassifyDevice(DriverInfo device)
    {
        var name = device.Name ?? string.Empty;
        var type = device.DeviceClass ?? string.Empty;
        bool named(string word) => name.Contains(word, StringComparison.OrdinalIgnoreCase);
        bool isClass(string value) => type.Equals(value, StringComparison.OrdinalIgnoreCase);

        if (NvidiaHardwareDetector.IsNvidiaDevice(device))
        {
            if (isClass("Display") || named("GeForce") || named("RTX") ||
                named("Graphics") || named("GPU"))
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

        if (IntelHardwareDetector.IsIntelDevice(device))
        {
            if (isClass("Display") || named("Graphics"))
                return 0;
            if (isClass("Bluetooth") || named("Bluetooth"))
                return 1;
        }

        // Killer is a product branding, not a unique Intel vendor ID:
        // PCI\VEN_8086 alone cannot prove that this is a Killer adapter.
        if ((named("Killer") ||
             Contains(device.Manufacturer, "Killer") ||
             Contains(device.DriverProvider, "Killer")) &&
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
        public int SoftwareTitles { get; set; }
    }
}
