using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Safe, per-device read of Win32_PnPEntity.ConfigManagerErrorCode.
/// An absent/malformed optional WMI variant is NOT Windows problem code 0.
/// Do not expose raw COM exception messages, PnP paths or device IDs.
/// </summary>
public static class WmiPnpProblemCodeReader
{
    public sealed record Result(uint Code, bool Verified);

    public static Result Read(Func<object?> getter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        object? raw;
        try
        {
            raw = getter();
        }
        catch (Exception exception) when (
            exception is ManagementException or COMException or
            UnauthorizedAccessException or SecurityException)
        {
            // The REQUIRED Win32_PnPEntity query still controls whether
            // the inventory is complete; this ONE field is optional.
            return new Result(0, false);
        }

        return TryRead(raw, out var code)
            ? new Result(code, true)
            : new Result(0, false);
    }

    private static bool TryRead(object? raw, out uint code)
    {
        switch (raw)
        {
            case uint value:
                code = value;
                return true;
            case int value when value >= 0:
                code = (uint)value;
                return true;
            case long value when value >= 0 && value <= uint.MaxValue:
                code = (uint)value;
                return true;
            case ulong value when value <= uint.MaxValue:
                code = (uint)value;
                return true;
            case short value when value >= 0:
                code = (uint)value;
                return true;
            case ushort value:
                code = value;
                return true;
            case byte value:
                code = value;
                return true;
            case sbyte value when value >= 0:
                code = (uint)value;
                return true;
            case string value when value.Length is > 0 and <= 10 &&
                value.All(char.IsAsciiDigit) &&
                uint.TryParse(value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var parsed):
                code = parsed;
                return true;
            default:
                code = 0;
                return false;
        }
    }
}
