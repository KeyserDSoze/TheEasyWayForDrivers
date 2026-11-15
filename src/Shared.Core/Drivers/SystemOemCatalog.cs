using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

public static class SystemOemCatalog
{
    // WMI/SMBIOS strings are untrusted; a substring in "NotDell" or
    // "Other Hewlett PC" does NOT attest the system's manufacturer.
    public static bool IsDellManufacturer(string? manufacturer) =>
        HasManufacturerPrefix(manufacturer, "Dell");

    public static bool IsHpManufacturer(string? manufacturer)
    {
        if (manufacturer is null ||
            manufacturer.Trim(' ').StartsWith(
                "Hewlett Packard Enterprise",
                StringComparison.OrdinalIgnoreCase) ||
            manufacturer.Trim(' ').StartsWith(
                "Hewlett-Packard Enterprise",
                StringComparison.OrdinalIgnoreCase))
            return false;

        return HasManufacturerPrefix(manufacturer, "HP") ||
            HasManufacturerPrefix(manufacturer, "Hewlett-Packard") ||
            HasManufacturerPrefix(manufacturer, "Hewlett Packard");
    }

    // #476: routing and opt-in companion guards must share an anchored
    // manufacturer identity, including for OEMs beyond Dell and HP.
    public static bool IsLenovoManufacturer(string? manufacturer) =>
        HasManufacturerPrefix(manufacturer, "Lenovo");

    public static bool IsMicrosoftSurfacePc(
        string? manufacturer, string? model) =>
        HasManufacturerPrefix(manufacturer, "Microsoft") &&
        (HasManufacturerPrefix(model, "Surface") ||
         HasManufacturerPrefix(model, "Microsoft Surface"));

    private static bool HasManufacturerPrefix(string? manufacturer, string prefix)
    {
        if (string.IsNullOrWhiteSpace(manufacturer) ||
            manufacturer.Length > 160 ||
            manufacturer.Any(ch => char.IsControl(ch) ||
                char.IsSurrogate(ch) ||
                (char.IsWhiteSpace(ch) && ch != ' ') ||
                char.GetUnicodeCategory(ch) is
                    System.Globalization.UnicodeCategory.Format or
                    System.Globalization.UnicodeCategory.LineSeparator or
                    System.Globalization.UnicodeCategory.ParagraphSeparator))
            return false;

        // Only benign ASCII edge spaces. Tabs, controls, bidi and invisible
        // format marks must not be trimmed into a valid manufacturer ID.
        var value = manufacturer.Trim(' ');
        return value.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
             value.Length > prefix.Length &&
             value[prefix.Length] is ' ' or '.' or ',');
    }

    public const string PcSpecialistDriversUrl =
        "https://www.pcspecialist.co.uk/download-drivers/";

    public static bool IsPcSpecialistManufacturer(string? manufacturer)
    {
        return HasManufacturerPrefix(manufacturer, "PCSpecialist") ||
            HasManufacturerPrefix(manufacturer, "PC Specialist");
    }

    public static SystemOemDescriptor? Match(
        string? manufacturer,
        string? model)
    {
        // Validate the original manufacturer and model values before any
        // whitespace normalization can repair an invalid WMI identity.

        // The Recoil/Elimina/Ionico ranges are custom-built PCs. Their
        // factory driver sets are selected by order on the official site,
        // not by the laptop marketing name alone.
        if (IsPcSpecialistManufacturer(manufacturer))
        {
            return new(
                "pcspecialist",
                "PCSpecialist",
                PcSpecialistDriversUrl,
                "Portale driver PCSpecialist",
                false);
        }

        // Validate the original WMI string before a generic Trim() can
        // hide TAB/newline/format markers and fabricate an OEM identity.
        if (IsDellManufacturer(manufacturer))
        {
            return new(
                "dell",
                "Dell",
                "https://www.dell.com/support/home/",
                "SupportAssist",
                true);
        }

        if (IsLenovoManufacturer(manufacturer))
        {
            return new(
                "lenovo",
                "Lenovo",
                "https://support.lenovo.com/",
                "Lenovo System Update",
                true);
        }

        if (IsHpManufacturer(manufacturer))
        {
            return new(
                "hp",
                "HP",
                "https://support.hp.com/",
                "HP Support Assistant",
                true);
        }

        if (HasManufacturerPrefix(manufacturer, "ASUSTeK") ||
            HasManufacturerPrefix(manufacturer, "ASUS"))
        {
            return new(
                "asus",
                "ASUS",
                "https://www.asus.com/support/download-center/",
                "MyASUS",
                false);
        }

        if (HasManufacturerPrefix(manufacturer, "Acer"))
        {
            return new(
                "acer",
                "Acer",
                "https://www.acer.com/us-en/support/drivers-and-manuals",
                "Acer Care Center",
                true);
        }

        if (HasManufacturerPrefix(manufacturer, "Micro-Star") ||
            HasManufacturerPrefix(manufacturer, "MSI"))
        {
            return new(
                "msi",
                "MSI",
                "https://www.msi.com/support/download/",
                "MSI Center",
                true);
        }

        if (HasManufacturerPrefix(manufacturer, "Gigabyte"))
        {
            return new(
                "gigabyte",
                "GIGABYTE",
                "https://www.gigabyte.com/Support",
                "GIGABYTE Control Center",
                true);
        }

        if (IsMicrosoftSurfacePc(manufacturer, model))
        {
            return new(
                "surface",
                "Microsoft Surface",
                "https://support.microsoft.com/surface/drivers-firmware/download-drivers-and-firmware-for-surface",
                "Surface app",
                false);
        }

        return null;
    }

}
