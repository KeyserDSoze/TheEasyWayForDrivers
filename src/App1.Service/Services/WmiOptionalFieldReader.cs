using System.Management;
using System.Runtime.InteropServices;
using System.Security;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Bounded per-property reader for optional WMI scalar fields. Prevents
/// one broken Win32_PnPEntity or Win32_PnPSignedDriver COM getter from
/// invalidating other devices or the complete signed metadata query.
/// Raw variants, exception messages and device identifiers are never logged.
/// </summary>
public static class WmiOptionalFieldReader
{
    public const int MaximumTextLength = 1024;

    public static string? ReadString(Func<object?> getter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        object? value;
        try
        {
            value = getter();
        }
        catch (Exception exception) when (
            exception is ManagementException or COMException or
            UnauthorizedAccessException or SecurityException or
            InvalidCastException)
        {
            // The optional field is unavailable, not proof of an absent
            // driver or a healthy device. Another field/row may still be
            // valid. Never include a COM exception's private message.
            return null;
        }

        // WMI variants must be actual text. Calling ToString() on an
        // arbitrary COM object may execute code and/or expose user data.
        if (value is not string text ||
            text.Length is 0 or > MaximumTextLength ||
            string.IsNullOrWhiteSpace(text) ||
            text.Any(char.IsControl))
            return null;

        return text;
    }

    public static bool ReadBoolean(Func<object?> getter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        try
        {
            // Missing or malformed IsSigned does not establish a valid
            // Authenticode/CAT signature and never becomes true.
            return getter() is true;
        }
        catch (Exception exception) when (
            exception is ManagementException or COMException or
            UnauthorizedAccessException or SecurityException or
            InvalidCastException)
        {
            return false;
        }
    }
}
