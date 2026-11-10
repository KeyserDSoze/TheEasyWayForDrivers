namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Strict, read-only recovery of NON-PCI bus/model IDs from the SAME PnP
/// instance's native SetupAPI record when WMI HardwareID is nonempty but
/// only gives USB VID/class, audio FUNC/VEN, SWC VEN, HID class/VID
/// or a generic USBSTOR device type (GenDisk/Disk/GenCdRom/RAW).
/// Exact-device SetupAPI recovery never uses friendly names or OEM hints.
/// Never use display names, manufacturer strings or another instance.
/// </summary>
internal static class PnpNonPciModelRecovery
{
    internal readonly record struct Scope(
        string Bus, string? Vendor, string? Function);

    internal static bool TryGetScope(
        IReadOnlyList<string> validWmiHardware, out Scope result)
    {
        result = default;
        if (validWmiHardware.Count == 0)
            return false;

        Scope? scope = null;
        foreach (var id in validWmiHardware)
        {
            if (!TryParseIncomplete(id, out var part))
                return false;

            if (scope is not { } previous)
            {
                scope = part;
                continue;
            }

            if (!previous.Bus.Equals(part.Bus,
                    StringComparison.OrdinalIgnoreCase) ||
                (previous.Vendor is not null && part.Vendor is not null &&
                    !previous.Vendor.Equals(part.Vendor,
                        StringComparison.OrdinalIgnoreCase)) ||
                (previous.Function is not null && part.Function is not null &&
                    !previous.Function.Equals(part.Function,
                        StringComparison.OrdinalIgnoreCase)))
                return false;

            scope = new Scope(previous.Bus, previous.Vendor ?? part.Vendor,
                previous.Function ?? part.Function);
        }

        result = scope!.Value;
        return true;
    }

    internal static bool IsMatchingNativeModel(string? native, Scope scope)
    {
        if (!PnpIdentifierFallback.IsStrictSingleId(native))
            return false;
        // HID is not a Catalog-search bus. The native ID must be a strict
        // HID VID+PID model; a WMI VID additionally constrains its vendor.
        // Both properties belong to the SAME PnP instance.
        if (scope.Bus == "HID")
            return native!.StartsWith(@"HID\VID_",
                    StringComparison.OrdinalIgnoreCase) &&
                HidPnpMatchPolicy.IsSpecificHardwareId(native) &&
                (scope.Vendor is null ||
                    native.StartsWith(
                        @"HID\VID_" + scope.Vendor + "&PID_",
                        StringComparison.OrdinalIgnoreCase));

        if (scope.Bus == "USBSTOR")
        {
            // USBSTOR generic compatible classes identify a device TYPE,
            // never a specific unit. Only a real, complete product ID
            // returned from the same instance's native SetupAPI record is
            // allowed. A WMI Disk/GenDisk class cannot borrow a CdRom
            // or other type; RAW has no type claim and remains constrained
            // to the native instance and a validated model.
            if (!UsbStorPnpMatchPolicy.IsSpecificHardwareId(native))
                return false;

            if (scope.Function is null)
                return true;

            var typePrefix = @"USBSTOR\" + scope.Function;
            return native!.Length > typePrefix.Length &&
                native.StartsWith(typePrefix,
                    StringComparison.OrdinalIgnoreCase);
        }

        // This parser validates the original complete ID, including exact
        // suffix grammar and the single bus separator. Model extraction
        // here is ONLY a gate; the ORIGINAL ID is retained for correlation.
        var publicModel = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(
            native);
        if (publicModel is null)
            return false;

        switch (scope.Bus)
        {
            case "USB":
                return publicModel.StartsWith(
                        @"USB\VID_", StringComparison.OrdinalIgnoreCase) &&
                    (scope.Vendor is null || publicModel.StartsWith(
                        @"USB\VID_" + scope.Vendor + "&PID_",
                        StringComparison.OrdinalIgnoreCase));
            case "HDAUDIO":
            case "INTELAUDIO":
                var prefix = scope.Bus + @"\FUNC_" +
                    scope.Function + "&VEN_";
                return publicModel.StartsWith(prefix,
                        StringComparison.OrdinalIgnoreCase) &&
                    (scope.Vendor is null || publicModel.StartsWith(
                        prefix + scope.Vendor + "&DEV_",
                        StringComparison.OrdinalIgnoreCase));
            case "SWC":
                return publicModel.StartsWith(
                        @"SWC\VEN_", StringComparison.OrdinalIgnoreCase) &&
                    (scope.Vendor is null || publicModel.StartsWith(
                        @"SWC\VEN_" + scope.Vendor + "&DEV_",
                        StringComparison.OrdinalIgnoreCase));
            default:
                return false;
        }
    }

    private static bool TryParseIncomplete(string id, out Scope result)
    {
        result = default;
        var separator = id.IndexOf('\\');
        if (separator is < 1 || id.IndexOf('\\', separator + 1) >= 0)
            return false;
        var bus = id[..separator].ToUpperInvariant();
        var key = id[(separator + 1)..].ToUpperInvariant();

        switch (bus)
        {
            case "USBSTOR":
                if (key == "RAW")
                {
                    result = new Scope(bus, null, null);
                    return true;
                }

                // Accept only EXACT Windows USBSTOR compatible class IDs,
                // not a producer prefix such as DiskSEAGATE_ or an
                // arbitrary GenDisk&FAKE identifier.
                var storageType = key.StartsWith("GEN",
                        StringComparison.Ordinal)
                    ? key[3..] : key;
                if (storageType is "DISK" or "CDROM" or "SFLOPPY" or
                    "OPTICAL" or "CHANGER" or "SEQUENTIAL" or
                    "USBSTOROTHER" or "WORM" or "OTHER")
                {
                    result = new Scope(bus, null, storageType);
                    return true;
                }
                return false;

            case "HID":
                if (key.Length == 8 &&
                    key.StartsWith("VID_", StringComparison.Ordinal) &&
                    IsHex(key.AsSpan(4)))
                {
                    result = new Scope(bus, key[4..], null);
                    return true;
                }

                // Only known shared Windows HID class compatible tokens.
                // Unknown/malformed class prefixes fail closed.
                if (key is "HID_DEVICE" or
                    "HID_DEVICE_SYSTEM_KEYBOARD" or
                    "HID_DEVICE_SYSTEM_MOUSE" ||
                    key.StartsWith("HID_DEVICE_UP_",
                        StringComparison.Ordinal) &&
                    key.Length == "HID_DEVICE_UP_".Length + 4 &&
                    IsHex(key.AsSpan("HID_DEVICE_UP_".Length)))
                {
                    result = new Scope(bus, null, null);
                    return true;
                }
                return false;

            case "USB":
                if (key.StartsWith("VID_", StringComparison.Ordinal) &&
                    key.Length == 8 && IsHex(key.AsSpan(4)))
                {
                    result = new Scope(bus, key[4..], null);
                    return true;
                }

                if (IsUsbClassOnly(key))
                {
                    result = new Scope(bus, null, null);
                    return true;
                }
                return false;

            case "HDAUDIO":
            case "INTELAUDIO":
                if (key.Length < 7 ||
                    !key.StartsWith("FUNC_", StringComparison.Ordinal) ||
                    !IsHex(key.AsSpan(5, 2)))
                    return false;
                var function = key.Substring(5, 2);
                if (key.Length == 7)
                {
                    result = new Scope(bus, null, function);
                    return true;
                }
                if (key.Length == 16 &&
                    key.AsSpan(7, 5).SequenceEqual("&VEN_") &&
                    IsHex(key.AsSpan(12, 4)))
                {
                    result = new Scope(bus, key[12..], function);
                    return true;
                }
                return false;

            case "SWC":
                if (key.StartsWith("VEN_", StringComparison.Ordinal) &&
                    key.Length == 8 && IsHex(key.AsSpan(4)))
                {
                    result = new Scope(bus, key[4..], null);
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

    private static bool IsUsbClassOnly(string key)
    {
        if (!key.StartsWith("CLASS_", StringComparison.Ordinal) ||
            key.Length < 8 || !IsHex(key.AsSpan(6, 2)))
            return false;
        var rest = key[8..];
        if (rest.Length == 0)
            return true;
        const string subclass = "&SUBCLASS_";
        if (!rest.StartsWith(subclass, StringComparison.Ordinal) ||
            rest.Length < subclass.Length + 2 ||
            !IsHex(rest.AsSpan(subclass.Length, 2)))
            return false;
        rest = rest[(subclass.Length + 2)..];
        return rest.Length == 0 ||
            (rest.Length == 8 &&
                rest.StartsWith("&PROT_", StringComparison.Ordinal) &&
                IsHex(rest.AsSpan(6, 2)));
    }

    private static bool IsHex(ReadOnlySpan<char> text) =>
        text.Length > 0 && text.ToString().All(Uri.IsHexDigit);
}
