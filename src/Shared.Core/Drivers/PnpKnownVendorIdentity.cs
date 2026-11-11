using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Determines the read-only OEM support vendor from strictly validated
/// Windows public PnP *models*. A Windows-installed driver provider or
/// friendly name can be stale, particularly for DCH audio/SWC and USB.
/// This classification cannot authorize installation or query a catalog.
/// </summary>
public static class PnpKnownVendorIdentity
{
    public enum Vendor
    {
        Nvidia, Intel, Realtek, Amd, Qualcomm, Broadcom, MediaTek,
        Logitech, Razer,
        Conflicting
    }

    /// <summary>
    /// Primary PnP HardwareIDs outrank CompatibleIDs when at least one
    /// recognized model exists. Contradictory primary vendors fail closed.
    /// Unknown/malformed models cannot invent a vendor.
    /// </summary>
    public static Vendor? FromDriver(DriverInfo driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        var hardware = ReadRecognized(driver.HardwareIds);
        if (hardware is not null)
            return hardware;

        return ReadRecognized(driver.CompatibleIds);
    }

    private static Vendor? ReadRecognized(IEnumerable<string> ids)
    {
        Vendor? result = null;
        foreach (var id in ids)
        {
            var vendor = FromId(id);
            if (vendor is null)
                continue;

            if (result is not null && result != vendor)
                return Vendor.Conflicting;
            result = vendor;
        }
        return result;
    }

    public static Vendor? FromId(string? id)
    {
        var model = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id);
        if (model is null)
            return null;

        if (model.StartsWith(@"ACPI\NVDA", StringComparison.Ordinal))
            return Vendor.Nvidia;

        if (model.StartsWith(@"PCI\VEN_", StringComparison.Ordinal))
            return FromCode(model.Substring(@"PCI\VEN_".Length, 4), isUsb: false);
        // Exact VID+PID models, never VID-only/HID class/usage tokens,
        // can identify Logitech/Razer for a manual OEM support handoff.
        // This is not WUA evidence or proof of an applicable driver.
        if (model.StartsWith(@"HID\VID_", StringComparison.Ordinal))
            return FromPeripheralCode(model.Substring(@"HID\VID_".Length, 4));
        if (model.StartsWith(@"USB\VID_", StringComparison.Ordinal))
        {
            var code = model.Substring(@"USB\VID_".Length, 4);
            return FromPeripheralCode(code) ?? FromCode(code, isUsb: true);
        }
        if (model.StartsWith(@"SWC\VEN_", StringComparison.Ordinal))
            return FromCode(model.Substring(@"SWC\VEN_".Length, 4), isUsb: false);

        if (model.StartsWith(@"HDAUDIO\FUNC_", StringComparison.Ordinal) ||
            model.StartsWith(@"INTELAUDIO\FUNC_", StringComparison.Ordinal))
        {
            // The model parser already checked the full FUNC+VEN+DEV
            // syntax and rejected private instance/unknown suffixes.
            var position = model.IndexOf("&VEN_", StringComparison.Ordinal);
            if (position >= 0)
                return FromCode(model.Substring(position + 5, 4), isUsb: false);
        }
        // SoundWire MAN is a bus-specific manufacturer code, NOT a PCI
        // VEN or USB VID. Recognize only validated Realtek codec models;
        // a generic aggregator, class-only token or unknown MAN remains
        // non-authoritative. The public model parser already removed
        // SUBSYS/UID without changing the original exact PnP match.
        if (model.StartsWith(@"SNDW\CLASS_", StringComparison.Ordinal) ||
            model.StartsWith(@"SOUNDWIRE\SDCA_", StringComparison.Ordinal))
            return model.Contains("&MAN_025D", StringComparison.Ordinal)
                ? Vendor.Realtek : null;

        return null;
    }

    private static Vendor? FromPeripheralCode(string code) =>
        code switch
        {
            "046D" => Vendor.Logitech,
            "1532" => Vendor.Razer,
            _ => null
        };

    private static Vendor? FromCode(string code, bool isUsb)
    {
        if (isUsb)
            return code switch
            {
                "0955" => Vendor.Nvidia,
                "8087" => Vendor.Intel,
                "0BDA" => Vendor.Realtek,
                "0CF3" or "05C6" => Vendor.Qualcomm,
                "0A5C" => Vendor.Broadcom,
                "0E8D" => Vendor.MediaTek,
                _ => null
            };

        return code switch
        {
            "10DE" => Vendor.Nvidia,
            "8086" or "1A56" => Vendor.Intel,
            "10EC" => Vendor.Realtek,
            "1002" or "1022" => Vendor.Amd,
            "168C" or "17CB" => Vendor.Qualcomm,
            "14E4" => Vendor.Broadcom,
            "14C3" => Vendor.MediaTek,
            _ => null
        };
    }
}
