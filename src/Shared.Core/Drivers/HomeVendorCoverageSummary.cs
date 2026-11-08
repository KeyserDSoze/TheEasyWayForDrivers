using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Three brief, privacy-safe status cards for the main scan results screen.
/// A classified PnP device, an official support URL and a WUA candidate
/// are THREE different kinds of evidence. No OEM catalog is queried here,
/// and these cards never authorize installation or replace WUA results.
/// </summary>
public static class HomeVendorCoverageSummary
{
    public sealed record Row(
        string Group,
        string DeviceStatus,
        string WindowsStatus,
        bool HasLocalDevice = false)
    {
        public bool IsInstallable => false;
        // Only enables a read-only, user-initiated Windows Driver Store
        // lookup. This never grants WUA or external OEM installation.
        public bool CanInspectLocalDrivers => HasLocalDevice;
    }

    private enum Category { Nvidia, IntelKiller, Realtek }

    public static IReadOnlyList<Row> Create(
        IEnumerable<DriverInfo> devices,
        DriverSearchReport? search,
        bool inventoryComplete)
    {
        ArgumentNullException.ThrowIfNull(devices);

        // Inventory lists may contain the same PnP node more than once.
        // Do not count a duplicated instance as another device.
        var known = inventoryComplete
            ? devices.Where(x => !string.IsNullOrWhiteSpace(x.DeviceId))
                .DistinctBy(x => x.DeviceId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
        var available = new[]
        {
            new List<DriverInfo>(), new List<DriverInfo>(), new List<DriverInfo>()
        };

        foreach (var device in known)
        {
            var category = Classify(device);
            if (category.HasValue)
                available[(int)category.Value].Add(device);
        }

        var wuaQueried = search is { AllFailed: false };
        // These are confirmed WUA Type=Driver GUIDs only: Type=Software,
        // partial result titles, OEM URLs and release news never enter.
        var confirmed = wuaQueried
            ? WuaDiagnosticAssociations.Create(
                search!.Updates.Where(update => Guid.TryParse(update.Id, out _)))
            : [];

        string DevicesText(int count) => !inventoryComplete
            ? "Inventario PnP non verificato"
            : count == 1
                ? "1 dispositivo PnP classificato"
                : $"{count} dispositivi PnP classificati";

        string WindowsText(IReadOnlyList<DriverInfo> categoryDevices)
        {
            if (!inventoryComplete)
                return "Correlazione WUA non disponibile";
            if (!wuaQueried)
                return "Ricerca WUA non verificata";

            var matched = confirmed.Count(update =>
                categoryDevices.Any(device =>
                    update.Matches(device)));
            var caveat = search!.IsPartial
                ? " · fonti Windows parziali"
                : " · solo fonti Windows";
            return matched == 1
                ? "1 candidato WUA per ID" + caveat
                : $"{matched} candidati WUA per ID" + caveat;
        }

        var groups = new[] { "NVIDIA", "Intel / Killer", "Realtek" };
        return groups.Select((group, i) => new Row(
            group, DevicesText(available[i].Count),
            WindowsText(available[i]), inventoryComplete &&
            available[i].Count > 0)).ToArray();
    }

    /// <summary>
    /// Pick an actual PnP node from the scan for a direct, opt-in
    /// SetupAPI/PnPUtil Driver Store lookup. Use the SAME classification
    /// as the Home cards; never pick by similar name across vendors.
    /// Prefer the GPU (not NVIDIA HDMI audio), Killer Wi-Fi (not an Intel
    /// chipset bridge) and Realtek MEDIA audio (not Ethernet).
    /// </summary>
    public static DriverInfo? ChooseLocalDriverStoreDevice(
        IEnumerable<DriverInfo> devices, string group, bool inventoryComplete)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (!inventoryComplete)
            return null;
        Category? category = group switch
        {
            "NVIDIA" => Category.Nvidia,
            "Intel / Killer" => Category.IntelKiller,
            "Realtek" => Category.Realtek,
            _ => null
        };
        if (!category.HasValue)
            return null;

        return devices
            .Where(device => !string.IsNullOrWhiteSpace(device.DeviceId))
            .DistinctBy(device => device.DeviceId,
                StringComparer.OrdinalIgnoreCase)
            .Where(device => Classify(device) == category.Value)
            .OrderByDescending(device => DevicePriority(device, category.Value))
            .ThenByDescending(device => device.NeedsAttention)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int DevicePriority(DriverInfo device, Category category)
    {
        bool IsClass(string value) =>
            string.Equals(device.DeviceClass, value,
                StringComparison.OrdinalIgnoreCase);
        bool HasName(string value) =>
            device.Name.Contains(value, StringComparison.OrdinalIgnoreCase);

        return category switch
        {
            Category.Nvidia when IsClass("Display") => 120,
            Category.Nvidia when HasName("GeForce") || HasName("Graphics") ||
                HasName("GPU") => 110,
            Category.Nvidia => 20,
            Category.IntelKiller when IsClass("Net") && HasName("Killer") => 140,
            Category.IntelKiller when IsClass("Net") &&
                (HasName("Wi-Fi") || HasName("Wireless")) => 130,
            Category.IntelKiller when IsClass("Net") => 120,
            Category.IntelKiller when IsClass("Bluetooth") => 80,
            Category.IntelKiller when IsClass("Display") => 50,
            Category.IntelKiller => 20,
            Category.Realtek when IsClass("MEDIA") => 120,
            Category.Realtek when HasName("Audio") || HasName("Sound") => 110,
            Category.Realtek when IsClass("Net") => 50,
            Category.Realtek => 20,
            _ => 0
        };
    }

    private static Category? Classify(DriverInfo device)
    {
        // Prefer hardware-vendor identity where possible, including GPU
        // and audio nodes which may have Microsoft generic names.
        if (NvidiaHardwareDetector.IsNvidiaDevice(device))
            return Category.Nvidia;

        var vendor = VendorSourceDiscovery.GetRecognizedSupportVendor(device);
        if (vendor == "Realtek")
            return Category.Realtek;

        // Intel has many unrelated PCI chipset bridges. Limit the short
        // Home summary to display/network/Bluetooth, leaving all other
        // devices in the detailed PnP/OEM inventory.
        if ((vendor is "Intel" or "Killer / Intel") &&
            (device.DeviceClass is "Display" or "Net" or "Bluetooth" ||
             device.Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
             device.Name.Contains("Wireless", StringComparison.OrdinalIgnoreCase)))
            return Category.IntelKiller;

        return null;
    }
}
