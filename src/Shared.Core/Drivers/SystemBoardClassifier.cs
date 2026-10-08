namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Flags common motherboard model families without asserting that a system
/// is definitely custom built. Laptop and prebuilt models remain unclassified.
/// </summary>
public static class SystemBoardClassifier
{
    public static bool LooksLikeMotherboard(string? manufacturer, string? model)
    {
        var vendor = SystemIdentityNormalizer.Clean(manufacturer);
        var product = SystemIdentityNormalizer.Clean(model);
        if (vendor is null || product is null)
            return false;

        if (vendor.Contains("ASUSTeK", StringComparison.OrdinalIgnoreCase) ||
            vendor.StartsWith("ASUS", StringComparison.OrdinalIgnoreCase))
        {
            return StartsWithAny(product, "PRIME ", "TUF GAMING ", "ROG STRIX ", "PROART ", "ROG MAXIMUS ", "ROG CROSSHAIR ");
        }

        if (vendor.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) ||
            vendor.StartsWith("MSI", StringComparison.OrdinalIgnoreCase))
        {
            return StartsWithAny(product, "MAG B", "MAG X", "MPG B", "MPG X", "MEG X", "PRO B", "PRO H", "PRO Z");
        }

        if (vendor.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase))
        {
            return StartsWithAny(product, "B550 ", "B650 ", "B760 ", "X570 ", "X670 ", "X870 ", "Z690 ", "Z790 ", "Z890 ");
        }

        return false;
    }

    private static bool StartsWithAny(string value, params string[] prefixes) =>
        prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
