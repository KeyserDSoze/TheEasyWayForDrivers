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

    private static bool HasManufacturerPrefix(string? manufacturer, string prefix)
    {
        if (string.IsNullOrWhiteSpace(manufacturer) ||
            manufacturer.Length > 160 ||
            manufacturer.Any(ch => char.IsControl(ch) ||
                char.IsSurrogate(ch) ||
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
        var value = manufacturer?.Trim() ?? string.Empty;
        return StartsWithWord(value, "PCSpecialist") ||
            StartsWithWord(value, "PC Specialist");
    }

    public static SystemOemDescriptor? Match(
        string? manufacturer,
        string? model)
    {
        var value = manufacturer?.Trim() ?? string.Empty;

        // The Recoil/Elimina/Ionico ranges are custom-built PCs. Their
        // factory driver sets are selected by order on the official site,
        // not by the laptop marketing name alone.
        if (IsPcSpecialistManufacturer(value))
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

        if (Contains(value, "Lenovo"))
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

        if (Contains(value, "ASUSTeK") ||
            StartsWithWord(value, "ASUS"))
        {
            return new(
                "asus",
                "ASUS",
                "https://www.asus.com/support/download-center/",
                "MyASUS",
                false);
        }

        if (Contains(value, "Acer"))
        {
            return new(
                "acer",
                "Acer",
                "https://www.acer.com/us-en/support/drivers-and-manuals",
                "Acer Care Center",
                true);
        }

        if (Contains(value, "Micro-Star") ||
            StartsWithWord(value, "MSI"))
        {
            return new(
                "msi",
                "MSI",
                "https://www.msi.com/support/download/",
                "MSI Center",
                true);
        }

        if (Contains(value, "Gigabyte"))
        {
            return new(
                "gigabyte",
                "GIGABYTE",
                "https://www.gigabyte.com/Support",
                "GIGABYTE Control Center",
                true);
        }

        if (Contains(value, "Microsoft") &&
            Contains(model, "Surface"))
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

    private static bool Contains(
        string? value,
        string fragment) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(
            fragment,
            StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithWord(
        string value,
        string word) =>
        value.Equals(
            word,
            StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(
            word + " ",
            StringComparison.OrdinalIgnoreCase);
}
