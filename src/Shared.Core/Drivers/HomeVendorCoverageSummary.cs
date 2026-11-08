using TheEasyWayForDrivers.Core.Models;

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
        string WindowsStatus)
    {
        public bool IsInstallable => false;
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
            ? search!.Updates.Where(update => Guid.TryParse(update.Id, out _))
                .DistinctBy(update => update.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray()
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
                    DriverUpdateMatcher.IsMatch(device, update)));
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
            WindowsText(available[i]))).ToArray();
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
