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
                    item["Manufacturer"]?.ToString()?.Trim();

                var model =
                    item["Model"]?.ToString()?.Trim();

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
