namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Curated links to Microsoft's model-specific download pages, not downloadable
/// driver metadata or a guarantee of hardware/OS applicability.
/// </summary>
public static class SurfaceSupportCatalog
{
    public const string OfficialSupportPage =
        "https://support.microsoft.com/en-us/surface/drivers-firmware/download-drivers-and-firmware-for-surface";

    private static readonly IReadOnlyDictionary<string, int> ModelDownloads =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Surface Pro 8"] = 103503,
            ["Surface Pro 9 with Intel"] = 104680,
            ["Surface Pro 9 with 5G"] = 105941,
            ["Surface Pro 10 for Business"] = 105947,
            ["Surface Laptop Studio"] = 103505,
            ["Surface Laptop Studio 2"] = 105610,
            ["Surface Laptop 4 with AMD Processor"] = 102923,
            ["Surface Laptop 3 with Intel Processor"] = 100429
        };

    public static string Resolve(string? model)
    {
        if (!string.IsNullOrWhiteSpace(model) &&
            ModelDownloads.TryGetValue(model.Trim(), out var downloadId))
            return $"https://www.microsoft.com/download/details.aspx?id={downloadId}";

        // A partial name like "Surface Pro 9" can refer to Intel or ARM;
        // we must never guess when Windows reports an ambiguous label.
        return OfficialSupportPage;
    }

    public static bool HasExactMatch(string? model) =>
        !string.IsNullOrWhiteSpace(model) &&
        ModelDownloads.ContainsKey(model.Trim());
}
