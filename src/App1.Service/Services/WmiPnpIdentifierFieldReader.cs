using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using TheEasyWayForDrivers.Core.Drivers;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Per-device, read-only Win32_PnPEntity HardwareID/CompatibleID reader.
/// A broken OPTIONAL WMI field must not discard the rest of the PnP scan.
/// Only strict bus-qualified IDs can reach SetupAPI merge/WUA correlation.
/// </summary>
public static class WmiPnpIdentifierFieldReader
{
    public static IReadOnlyList<string> Read(Func<object?> getter)
    {
        ArgumentNullException.ThrowIfNull(getter);

        object? value;
        try
        {
            value = getter();
        }
        catch (Exception error) when (error is
            ManagementException or COMException or UnauthorizedAccessException
            or SecurityException or InvalidCastException)
        {
            // No WMI message, device instance path or serial in logs.
            // An empty category can be recovered from the same instance
            // through the existing native SetupAPI / signed-ID fallbacks.
            return [];
        }

        if (value is string single)
            return PnpSetupApiIdentity.SanitizeWmiIds([single]);

        if (value is not Array values || values.Rank != 1 ||
            values.Length > PnpSetupApiIdentity.MaximumIds)
            return [];

        // A WMI variant array may contain non-string values. Never call
        // ToString() on untrusted COM objects (or their private contents).
        // A later valid NVIDIA/Killer/Realtek ID still survives a bad entry.
        var raw = new List<string>(values.Length);
        foreach (var item in values)
        {
            if (item is string id)
                raw.Add(id);
        }

        return PnpSetupApiIdentity.SanitizeWmiIds(raw);
    }
}
