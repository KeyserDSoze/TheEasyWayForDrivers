using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

public static class SystemOemCatalog
{
    public static SystemOemDescriptor? Match(
        string? manufacturer,
        string? model)
    {
        var value = manufacturer?.Trim() ?? string.Empty;

        if (Contains(value, "Dell"))
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

        if (Contains(value, "Hewlett-Packard") ||
            StartsWithWord(value, "HP"))
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
