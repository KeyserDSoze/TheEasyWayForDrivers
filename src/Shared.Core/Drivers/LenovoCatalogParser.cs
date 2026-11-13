using System.Text.Json;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Interprets fields published by the Lenovo Client Scripting module.
/// Metadata only: no execution of descriptors, packages, or firmware.
/// </summary>
public static class LenovoCatalogParser
{
    public const int MaximumDriverResults = 50;

    // The Desktop Lenovo.Client.Scripting reader exports at most 1001
    // PackageType=2 rows as an overflow sentinel. Enforce the same
    // completeness boundary on IPC/saved JSON BEFORE filtering/dedup:
    // 50 is a fair UI preview, never a complete catalog.
    public const int MaximumReceivedRows = 1000;
    public sealed record Package(string Id, string Name, string Version, string Category,
        string ReleaseDate);

    public static IReadOnlyList<Package> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        if (json.Length > 1024 * 1024) throw new InvalidDataException("Lenovo metadata too large.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Null) return [];
        var entries = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().ToArray(),
            JsonValueKind.Object => [root],
            _ => throw new InvalidDataException("Invalid Lenovo response JSON shape.")
        };

        if (entries.Length > MaximumReceivedRows)
            throw new InvalidDataException(
                "Lenovo catalog exceeds the verifiable response row limit.");

        var results = new List<(Package Entry, string Category)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var type = Value(entry, "PackageType");
            // Only type 2 is the official Lenovo driver package type.
            if (type != "2") continue;
            var id = Value(entry, "ID");
            var name = Value(entry, "Name");
            if (id.Length == 0 || name.Length == 0 || !seen.Add(id))
                continue;
            var category = Value(entry, "Category");
            results.Add((new Package(id, name, Value(entry, "Version"),
                category, Value(entry, "ReleaseDate")), category));
        }
        return OemCatalogResultSelection.FairByCategory(
            results, MaximumDriverResults);
    }

    private static string Value(JsonElement row, string property)
    {
        if (!row.TryGetProperty(property, out var value)) return "";
        if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
            return "";
        var content = value.ToString().Replace('\r', ' ').Replace('\n', ' ').Trim();
        return content.Length > 160 ? content[..160] : content;
    }
}
