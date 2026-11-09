using System.Text.Json;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>Displays vendor-published metadata, never a verified installation plan.</summary>
public sealed record HpSoftpaqEntry(
    string Id,
    string Title,
    string Version,
    string ReleaseType,
    string PublishedDate);

public static class HpSoftpaqCatalogParser
{
    public const int MaximumDriverResults = 120;

    public static IReadOnlyList<HpSoftpaqEntry> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        if (json.Length > 1024 * 1024)
            throw new InvalidDataException("HP catalog response is too large.");

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            MaxDepth = 8
        });
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Null)
            return [];

        var entries = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().ToArray(),
            JsonValueKind.Object => [root],
            _ => throw new InvalidDataException("Unexpected HP catalog JSON format.")
        };

        // Inspect the whole bounded response BEFORE limiting driver rows.
        // Firmware, BIOS and software may precede valid drivers by hundreds
        // of entries in CMSL output.
        var result = new List<(HpSoftpaqEntry Entry, string Category)>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;
            var category = Field(entry, "Category");
            if (!IsDriverCategory(category))
                continue;
            var id = Field(entry, "Id");
            var title = Field(entry, "Name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) ||
                !seenIds.Add(id))
                continue;
            result.Add((new HpSoftpaqEntry(id, title,
                string.IsNullOrWhiteSpace(Field(entry, "Version")) ? "N/D" : Field(entry, "Version"),
                string.IsNullOrWhiteSpace(Field(entry, "ReleaseType")) ? "N/D" : Field(entry, "ReleaseType"),
                string.IsNullOrWhiteSpace(Field(entry, "ReleaseDate")) ? "N/D" : Field(entry, "ReleaseDate")), category));
        }

        return OemCatalogResultSelection.FairByCategory(
            result, MaximumDriverResults);
    }

    /// <summary>
    /// HP CMSL 1.8.5+ exposes driver subcategories such as
    /// "Driver - Graphics" or "Driver - Network". Do not confuse these
    /// with Driverpack, firmware, BIOS or software categories.
    /// </summary>
    public static bool IsDriverCategory(string? category)
    {
        var value = category?.Trim() ?? string.Empty;
        return value.Equals("Driver", StringComparison.OrdinalIgnoreCase) ||
            (value.StartsWith("Driver - ", StringComparison.OrdinalIgnoreCase) &&
             !string.IsNullOrWhiteSpace(value["Driver - ".Length..]));
    }

    private static string Field(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var value))
            return string.Empty;
        var text = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : value.ValueKind is JsonValueKind.Number or JsonValueKind.True
                or JsonValueKind.False ? value.ToString() : "";
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > 150 ? text[..150] : text;
    }
}
