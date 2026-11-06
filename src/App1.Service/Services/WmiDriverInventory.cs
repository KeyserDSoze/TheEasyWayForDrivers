using System.Globalization;
using System.Management;
using TheEasyWayForDrivers.Core.Abstractions;
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

        var drivers = new List<DriverInfo>();
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

                    signedLookup.Value.TryGetValue(deviceId, out var metadata);

                    drivers.Add(new DriverInfo(
                        deviceId,
                        GetString(item, "Name") ?? deviceId,
                        GetString(item, "Manufacturer"),
                        GetString(item, "PNPClass"),
                        metadata?.Provider,
                        metadata?.Version,
                        metadata?.Date,
                        metadata?.InfName,
                        metadata?.IsSigned ?? false,
                        metadata is not null,
                        GetUInt32(item, "ConfigManagerErrorCode"),
                        GetStringArray(item, "HardwareID"),
                        GetStringArray(item, "CompatibleID"),
                        signedLookup.Completed));
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
            "SELECT DeviceID, DriverProviderName, DriverVersion, DriverDate, InfName, IsSigned " +
            "FROM Win32_PnPSignedDriver"))
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

                    signedDrivers[deviceId] = new DriverMetadata(
                        GetString(item, "DriverProviderName"),
                        GetString(item, "DriverVersion"),
                        GetDate(item, "DriverDate"),
                        GetString(item, "InfName"),
                        GetBoolean(item, "IsSigned"));
                }
            }
        }

        return signedDrivers;
    }

    private static string? GetString(
        ManagementBaseObject item,
        string propertyName) =>
        item[propertyName]?.ToString();

    private static IReadOnlyList<string> GetStringArray(
        ManagementBaseObject item,
        string propertyName)
    {
        if (item[propertyName] is not Array values)
        {
            return [];
        }

        return values
            .Cast<object?>()
            .Select(value => value?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool GetBoolean(
        ManagementBaseObject item,
        string propertyName) =>
        item[propertyName] is bool value && value;

    private static uint GetUInt32(
        ManagementBaseObject item,
        string propertyName) =>
        item[propertyName] is null
            ? 0
            : Convert.ToUInt32(
                item[propertyName],
                CultureInfo.InvariantCulture);

    private static DateTimeOffset? GetDate(
        ManagementBaseObject item,
        string propertyName)
    {
        var value = GetString(item, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(
                ManagementDateTimeConverter.ToDateTime(value));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private sealed record DriverMetadata(
        string? Provider,
        string? Version,
        DateTimeOffset? Date,
        string? InfName,
        bool IsSigned);
}
