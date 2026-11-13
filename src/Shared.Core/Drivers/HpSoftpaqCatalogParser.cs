using System.Globalization;
using System.Text;
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

    // Keep this exact allowlist in sync with HpSoftpaqCatalogReader.Query.
    // A prefix is NOT evidence of an official HP driver category:
    // Driver - Firmware / Driver - BIOS / Driver - Networkx and arbitrary
    // legacy IPC category strings must not be presented as drivers.
    private static readonly HashSet<string> KnownDriverCategories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Driver",
            "Driver - Graphics",
            "Driver - Chipset",
            "Driver - Audio",
            "Driver - Keyboard, Mouse and Input Devices",
            "Driver - Enabling",
            "Driver - Network",
            "Driver - Storage",
            "Driver - Controller"
        };

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
            // Unlike display fields, never normalize arbitrary control
            // characters in Category before deciding it is a driver.
            var category = RawCategory(entry);
            if (!IsDriverCategory(category))
                continue;
            var id = SafeId(entry);
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
    /// HP CMSL 1.8.5+ exposes the specific subcategories requested by the
    /// installed-cmdlet reader. Metadata from JSON, IPC or older snapshots
    /// MUST fail closed for all other categories, even if prefixed "Driver -".
    /// </summary>
    public static bool IsDriverCategory(string? category)
    {
        if (category is null || category.Any(char.IsControl))
            return false;

        // Preserve benign ASCII padding; never trim Unicode/control tokens
        // into an official category that HP did not actually declare.
        return KnownDriverCategories.Contains(category.Trim(' '));
    }

    private static string RawCategory(JsonElement entry) =>
        entry.TryGetProperty("Category", out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    // CMSL SoftPaq identifiers are opaque ASCII tokens, not display text.
    // Never "repair" a malformed identifier (for example by stripping a
    // bidi override or TAB): it could then collide with a genuine SoftPaq.
    private static string SafeId(JsonElement entry)
    {
        if (!entry.TryGetProperty("Id", out var value))
            return string.Empty;
        var raw = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            _ => string.Empty
        };
        var id = raw.Trim(' ');
        return id.Length is > 0 and <= 150 &&
            id.All(ch => char.IsAsciiLetterOrDigit(ch) ||
                ch is '-' or '_' or '.')
            ? id : string.Empty;
    }

    private static string Field(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var value))
            return string.Empty;
        var text = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : value.ValueKind is JsonValueKind.Number or JsonValueKind.True
                or JsonValueKind.False ? value.ToString() : "";

        // Preserve international driver names but remove invisible control
        // and bidi-format characters from untrusted local CMSL JSON.
        // Unlike SafeId, human-readable metadata can be cleaned safely.
        var safe = new StringBuilder(Math.Min(text.Length, 150));
        foreach (var ch in text)
        {
            safe.Append(char.IsControl(ch) || char.IsSurrogate(ch) ||
                char.GetUnicodeCategory(ch) is UnicodeCategory.Format or
                    UnicodeCategory.LineSeparator or
                    UnicodeCategory.ParagraphSeparator
                ? ' ' : ch);
            if (safe.Length == 150)
                break;
        }
        return safe.ToString().Trim();
    }
}
