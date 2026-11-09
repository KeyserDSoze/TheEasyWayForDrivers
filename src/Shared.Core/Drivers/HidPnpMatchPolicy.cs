namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Exact HID PnP IDs are useful WUA/INF evidence ONLY if they identify a
/// specific product, rather than a universal HID keyboard/mouse class or
/// a VID without PID. This never proves applicability or authorizes install.
/// </summary>
public static class HidPnpMatchPolicy
{
    public static bool IsSpecificHardwareId(string? id)
    {
        if (!PnpIdentifierFallback.IsStrictSingleId(id) ||
            !id!.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase))
            return false;

        var key = id.AsSpan(@"HID\".Length);
        // HID_DEVICE, HID_DEVICE_SYSTEM_KEYBOARD/MOUSE and related
        // compatible class IDs are shared by different physical devices.
        if (key.StartsWith("HID_DEVICE", StringComparison.OrdinalIgnoreCase))
            return false;

        // Other Windows HID enumerator-specific tokens retain the previous
        // strict exact-ID behavior. Do not infer their manufacturer/model.
        if (!key.StartsWith("VID_", StringComparison.OrdinalIgnoreCase))
            return true;

        // HID\VID_XXXX&PID_YYYY followed only by recognized Windows HID
        // attributes. Do not turn VID-only, &FAKE, instance path or a
        // partial hexadecimal product code into per-device evidence.
        if (key.Length < 17 ||
            !IsHex(key.Slice(4, 4)) ||
            !key.Slice(8).StartsWith("&PID_",
                StringComparison.OrdinalIgnoreCase) ||
            !IsHex(key.Slice(13, 4)))
            return false;

        var rest = key[17..];
        while (!rest.IsEmpty)
        {
            if (rest[0] != '&')
                return false;
            rest = rest[1..];
            var next = rest.IndexOf('&');
            var field = next < 0 ? rest : rest[..next];
            if (!IsValidSuffix(field))
                return false;
            rest = next < 0 ? ReadOnlySpan<char>.Empty : rest[next..];
        }
        return true;
    }

    private static bool IsValidSuffix(ReadOnlySpan<char> suffix) =>
        suffix.Length == 8 &&
            suffix.StartsWith("REV_", StringComparison.OrdinalIgnoreCase) &&
            IsHex(suffix[4..]) ||
        suffix.Length == 5 && (
            (suffix.StartsWith("MI_", StringComparison.OrdinalIgnoreCase) &&
             IsHex(suffix[3..])) ||
            (suffix.StartsWith("IG_", StringComparison.OrdinalIgnoreCase) &&
             IsHex(suffix[3..])) ||
            (suffix.StartsWith("COL", StringComparison.OrdinalIgnoreCase) &&
             IsHex(suffix[3..])));

    private static bool IsHex(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return false;
        foreach (var c in value)
            if (!Uri.IsHexDigit(c))
                return false;
        return true;
    }
}
