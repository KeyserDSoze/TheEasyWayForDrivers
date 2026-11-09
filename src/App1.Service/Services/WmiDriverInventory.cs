using System.Management;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.ServiceApp.Services;

public sealed class WmiDriverInventory : IDriverInventory
{
    public Task<IReadOnlyList<DriverInfo>> GetInstalledDriversAsync(
        CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<DriverInfo>>(
            () => Scan(cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<DriverInfo> Scan(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A failure in Win32_PnPSignedDriver must never suppress the
        // independent Win32_PnPEntity hardware inventory. An interrupted
        // metadata enumeration is discarded in its entirety.
        var signedLookup = WmiOptionalMetadataQuery.Read<
            IReadOnlyDictionary<string, DriverMetadata>>(
                () => ReadSignedDriverMetadata(cancellationToken),
                new Dictionary<string, DriverMetadata>(
                    StringComparer.OrdinalIgnoreCase));

        cancellationToken.ThrowIfCancellationRequested();

        // A successful but entirely empty Win32_PnPSignedDriver response
        // provides no evidence about whether any PnP device has a driver.
        // It must be treated as unavailable metadata, not 300 missing drivers.
        var signedMetadataVerified =
            WmiOptionalMetadataQuery.HasUsableRows(signedLookup);

        var drivers = new List<DriverInfo>();
        // Optional native PnP source: one read-only SetupAPI device set
        // shared across this scan; no per-device process, registry writes,
        // driver staging or secondary device-name heuristics.
        using var setupApi = WindowsSetupApiPnpIdentityReader.TryOpen();
        var nativeChecks = 0;
        using (var searcher = new ManagementObjectSearcher(
            "SELECT PNPDeviceID, Name, Manufacturer, PNPClass, ConfigManagerErrorCode, " +
            "HardwareID, CompatibleID FROM Win32_PnPEntity"))
        using (var results = searcher.Get())
        {
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var deviceId = GetString(item, "PNPDeviceID");
                    if (string.IsNullOrWhiteSpace(deviceId))
                        continue;

                    // Only metadata indexed by THIS PnP instance can provide
                    // missing identity fields; never join by vendor or title.
                    signedLookup.Value.TryGetValue(deviceId, out var metadata);
                    // WMI can return a nonempty list containing ONLY
                    // malformed values or even an instance/path string.
                    // Such a list must not suppress per-instance read-only
                    // SetupAPI recovery for NVIDIA/Killer/Realtek.
                    // These two optional WMI variants can be malformed
                    // independently of the mandatory PNPDeviceID row. A
                    // failed field must not abort all remaining devices.
                    // Empty/invalid categories use same-instance SetupAPI.
                    var pnpHardware = WmiPnpIdentifierFieldReader.Read(
                        () => item["HardwareID"]);
                    var pnpCompatible = WmiPnpIdentifierFieldReader.Read(
                        () => item["CompatibleID"]);
                    // A nonempty WMI HardwareID list may contain only
                    // generic PCI class/vendor, USB VID/class, audio FUNC/VEN
                    // or DCH SWC VEN tokens rather than a complete model.
                    // Ask the SAME PnP instance's read-only SetupAPI record,
                    // preserving WMI precedence and rejecting foreign
                    // native bus, vendor or audio function.
                    // Bounded to 2048 native device lookups per scan.
                    var needsHardware = pnpHardware.Count == 0 ||
                        PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
                            pnpHardware);
                    var needsCompatible = pnpCompatible.Count == 0;
                    WindowsSetupApiPnpIdentityReader.Identity? native = null;
                    if ((needsHardware || needsCompatible) &&
                        nativeChecks++ < 2048)
                        native = setupApi?.ReadMissing(
                            deviceId, needsHardware,
                            needsCompatible, cancellationToken);
                    var ids = PnpSetupApiIdentity.Merge(
                        pnpHardware, pnpCompatible,
                        native?.HardwareIds ?? [],
                        native?.CompatibleIds ?? [],
                        metadata?.HardwareId, metadata?.CompatibleId);

                    // Windows may expose no friendly name, an empty name,
                    // or the full PNPDeviceID as Name. Never display the
                    // instance/serial in the UI or OEM support handoffs;
                    // retain deviceId and PnP hardware IDs separately for
                    // accurate NVIDIA/Killer/Realtek matching.
                    var deviceClass = GetString(item, "PNPClass");
                    var safeName = PnpDeviceDisplayName.Create(
                        GetString(item, "Name"), deviceClass, deviceId);
                    // ConfigManagerErrorCode is a per-device WMI field.
                    // An invalid variant or COM getter must not abort the
                    // independent PnP inventory or claim Windows code 0.
                    var problem = WmiPnpProblemCodeReader.Read(
                        () => item["ConfigManagerErrorCode"]);
                    drivers.Add(new DriverInfo(
                        deviceId,
                        safeName,
                        GetString(item, "Manufacturer"),
                        deviceClass,
                        metadata?.Provider,
                        metadata?.Version,
                        metadata?.Date,
                        metadata?.InfName,
                        metadata?.IsSigned ?? false,
                        metadata is not null,
                        problem.Code,
                        ids.HardwareIds,
                        ids.CompatibleIds,
                        signedMetadataVerified,
                        ids.UsedSignedDriver,
                        ids.UsedSetupApi,
                        problem.Verified));
                }
            }
        }

        // A successful COM enumeration with zero usable PnP instances is
        // NOT evidence that this Windows PC has no devices.
        if (drivers.Count == 0)
            throw new InvalidDataException(
                "Inventario PnP vuoto: la scansione dei dispositivi non è verificata.");

        return drivers
            .OrderByDescending(driver => driver.NeedsAttention)
            .ThenBy(
                driver => driver.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, DriverMetadata>
        ReadSignedDriverMetadata(CancellationToken cancellationToken)
    {
        var signedDrivers = new Dictionary<string, DriverMetadata>(
            StringComparer.OrdinalIgnoreCase);

        using (var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, DriverProviderName, DriverVersion, DriverDate, InfName, IsSigned, " +
            "HardWareID, CompatID FROM Win32_PnPSignedDriver"))
        using (var results = searcher.Get())
        {
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var deviceId = GetString(item, "DeviceID");
                    if (string.IsNullOrWhiteSpace(deviceId))
                        continue;

                    // A malformed optional DMTF DriverDate must not
                    // erase this signed-driver row or the whole PnP scan.
                    var fields = WmiSignedDriverFieldReader.Create(
                        GetString(item, "DriverProviderName"),
                        GetString(item, "DriverVersion"),
                        GetString(item, "DriverDate"),
                        GetString(item, "InfName"),
                        GetBoolean(item, "IsSigned"));
                    signedDrivers[deviceId] = new DriverMetadata(
                        fields.Provider,
                        fields.Version,
                        fields.Date,
                        fields.InfName,
                        fields.IsSigned,
                        GetString(item, "HardWareID"),
                        GetString(item, "CompatID"));
                }
            }
        }

        return signedDrivers;
    }

    private static string? GetString(
        ManagementBaseObject item,
        string propertyName) =>
        item[propertyName]?.ToString();

    private static bool GetBoolean(
        ManagementBaseObject item,
        string propertyName) =>
        item[propertyName] is bool value && value;

    private sealed record DriverMetadata(
        string? Provider,
        string? Version,
        DateTimeOffset? Date,
        string? InfName,
        bool IsSigned,
        string? HardwareId,
        string? CompatibleId);
}
