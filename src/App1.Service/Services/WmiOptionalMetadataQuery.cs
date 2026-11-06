using System.Management;
using System.Runtime.InteropServices;
using System.Security;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Signed-driver WMI metadata is an optional enrichment of the authoritative
/// Win32_PnPEntity inventory, not a prerequisite for enumerating hardware.
/// An exception partway through this query invalidates the entire metadata
/// snapshot; caller must not reuse partially collected values.
/// </summary>
public static class WmiOptionalMetadataQuery
{
    public sealed record Result<T>(T Value, bool Completed);

    public static Result<T> Read<T>(Func<T> query, T empty)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            return new Result<T>(query(), true);
        }
        catch (Exception exception) when (
            exception is ManagementException or COMException or
            UnauthorizedAccessException or SecurityException)
        {
            // Do not expose WMI exception text containing device IDs or
            // host-specific strings. The PnP inventory can still be read.
            // Never reuse a dictionary partially filled before the failure.
            return new Result<T>(empty, false);
        }
    }
}
