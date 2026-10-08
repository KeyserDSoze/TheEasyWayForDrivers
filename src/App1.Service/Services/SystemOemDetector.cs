using System.Management;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.ServiceApp.Services;

public sealed class SystemOemDetector
{
    public SystemOemInfo Detect()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Manufacturer, Model FROM Win32_ComputerSystem");

        using var results = searcher.Get();

        foreach (ManagementObject item in results)
        {
            using (item)
            {
                var manufacturer =
                    SystemIdentityNormalizer.Clean(item["Manufacturer"]?.ToString());

                var model =
                    SystemIdentityNormalizer.Clean(item["Model"]?.ToString());

                var descriptor =
                    SystemOemCatalog.Match(
                        manufacturer,
                        model);

                return descriptor is null
                    ? new SystemOemInfo(
                        false,
                        null,
                        manufacturer,
                        model)
                    : new SystemOemInfo(
                        true,
                        descriptor,
                        manufacturer,
                        model);
            }
        }

        return new SystemOemInfo(
            false,
            null,
            null,
            null);
    }
}

public sealed record SystemOemInfo(
    bool IsRecognized,
    SystemOemDescriptor? Descriptor,
    string? RawManufacturer,
    string? Model);
