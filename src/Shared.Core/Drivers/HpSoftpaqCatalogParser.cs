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

        var result = new List<HpSoftpaqEntry>();
        foreach (var entry in entries.Take(50))
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;
            var category = Field(entry, "Category");
            if (!string.Equals(category, "Driver", StringComparison.OrdinalIgnoreCase))
                continue;
            var id = Field(entry, "Id");
            var title = Field(entry, "Name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                continue;
            result.Add(new HpSoftpaqEntry(id, title,
                Field(entry, "Version"), Field(entry, "ReleaseType"),
                Field(entry, "ReleaseDate")));
        }

        return result;
    }

    private static string Field(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var value))
            return "N/D";
        var text = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : value.ValueKind is JsonValueKind.Number or JsonValueKind.True
                or JsonValueKind.False ? value.ToString() : "";
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > 150 ? text[..150] : text;
    }
}
