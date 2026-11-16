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

    // HpSoftpaqCatalogReader fetches at most 1001 filtered records and
    // fails if more than 1000 are returned. Older IPC, saved responses and
    // direct callers must enforce that SAME completeness boundary. The
    // 120-row category-fair display is a PREVIEW, not a substitute for a
    // fully inspected CMSL catalog.
    public const int MaximumReceivedRows = 1000;

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

    // Metadata determining HP driver category, SoftPaq identity and
    // user-visible details may not be interpreted with JSON last-key-wins
    // semantics. Different casing is ambiguous too, even if the regular
    // JsonElement property lookup would ignore one of the spellings.
    private static readonly HashSet<string> CriticalMetadataKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Category", "Id", "Name", "Version", "ReleaseType", "ReleaseDate"
        };

    private static bool HasDuplicateCriticalMetadata(JsonElement entry)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in entry.EnumerateObject())
            if (CriticalMetadataKeys.Contains(property.Name) &&
                !seen.Add(property.Name))
                return true;

        return false;
    }

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

        // This limit is intentionally on the RECEIVED array, before type,
        // category, SoftPaq ID and duplicate filtering. An untrusted saved
        // payload cannot turn 1001+ records into an apparently complete
        // 120-row preview by marking early rows "Firmware" or invalid.
        if (entries.Length > MaximumReceivedRows)
            throw new InvalidDataException(
                "HP CMSL catalog contains too many records to verify completely.");

        // Inspect the whole bounded response BEFORE limiting driver rows.
        // Firmware, BIOS and software may precede valid drivers by hundreds
        // of entries in CMSL output.
        var result = new List<(HpSoftpaqEntry Entry, string Category)>();
        var seenIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;
            // #508: TryGetProperty silently selects one of multiple JSON
            // keys. A conflicting Firmware/Driver Category or SoftPaq Id
            // cannot be accepted or reserve a deduplication slot.
            // Do not read or echo any untrusted values from such a record.
            if (HasDuplicateCriticalMetadata(entry))
                continue;
            // Unlike display fields, never normalize arbitrary control
            // characters in Category before deciding it is a driver.
            var category = RawCategory(entry);
            if (!IsDriverCategory(category))
                continue;
            var id = SafeId(entry);
            var title = Field(entry, "Name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                continue;
            var candidate = new HpSoftpaqEntry(id, title,
                string.IsNullOrWhiteSpace(Field(entry, "Version")) ? "N/D" : Field(entry, "Version"),
                string.IsNullOrWhiteSpace(Field(entry, "ReleaseType")) ? "N/D" : Field(entry, "ReleaseType"),
                string.IsNullOrWhiteSpace(Field(entry, "ReleaseDate")) ? "N/D" : Field(entry, "ReleaseDate"));
            if (seenIds.TryGetValue(id, out var priorIndex))
            {
                // A private first CMSL title is redacted, not a useful
                // device name. Prefer the next safe title for the SAME
                // SoftPaq, including its actual driver subcategory.
                // Safe-vs-safe duplicates keep first-seen provenance.
                if (OemCatalogResultSelection.IsRedactedTitle(
                        result[priorIndex].Entry.Title) &&
                    !OemCatalogResultSelection.IsRedactedTitle(candidate.Title))
                    result[priorIndex] = (candidate, category);
                continue;
            }
            seenIds.Add(id, result.Count);
            result.Add((candidate, category));
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
        return SafeId(raw);
    }

    // Shared by the JSON parser and by public-package presentation paths.
    // An opaque SoftPaq ID must never be shortened or "repaired" into
    // another valid package before it is displayed or deduplicated.
    private static string SafeId(string? raw)
    {
        var id = raw?.Trim(' ') ?? string.Empty;
        return id.Length is > 0 and <= 150 &&
            id.All(ch => char.IsAsciiLetterOrDigit(ch) ||
                ch is '-' or '_' or '.')
            ? id : string.Empty;
    }

    internal static HpSoftpaqEntry ValidateEntry(HpSoftpaqEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var id = SafeId(entry.Id);
        var title = SafeDisplayText(entry.Title);
        if (id.Length == 0 || title.Length == 0)
            throw new InvalidDataException(
                "HP CMSL SoftPaq identity or title is not verifiable.");

        return entry with
        {
            Id = id,
            Title = title,
            Version = Fallback(SafeDisplayText(entry.Version)),
            ReleaseType = Fallback(SafeDisplayText(entry.ReleaseType)),
            PublishedDate = Fallback(SafeDisplayText(entry.PublishedDate))
        };
    }

    private static string Fallback(string value) =>
        value.Length == 0 ? "N/D" : value;

    private static string Field(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var value))
            return string.Empty;
        var text = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : value.ValueKind is JsonValueKind.Number or JsonValueKind.True
                or JsonValueKind.False ? value.ToString() : "";

        return SafeDisplayText(text);
    }

    private static string SafeDisplayText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        // Inspect the COMPLETE CMSL source value before its 150-character
        // display budget. A local user path, serial or PnP ID may be hidden
        // after a harmless-looking title prefix, especially on old IPC data.
        if (ContainsPrivateReference(raw))
            return "[dato riservato]";

        var safe = new StringBuilder(Math.Min(raw.Length, 150));
        foreach (var ch in raw)
        {
            safe.Append(char.IsWhiteSpace(ch) || char.IsControl(ch) ||
                char.IsSurrogate(ch) ||
                char.GetUnicodeCategory(ch) is UnicodeCategory.Format or
                    UnicodeCategory.LineSeparator or
                    UnicodeCategory.ParagraphSeparator
                ? ' ' : ch);
            if (safe.Length == 150)
                break;
        }
        return safe.ToString().Trim();
    }

    private static bool ContainsPrivateReference(string raw)
    {
        if (raw.Contains('\\') || raw.Contains('@') ||
            raw.Contains("://", StringComparison.Ordinal) ||
            raw.Contains("PRIVATE_", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith('/') ||
            raw.Contains("/Users/", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("/home/", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("/mnt/", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var marker in new[]
        {
            "VEN_", "VID_", "DEV_", "PID_", "SUBSYS_", "REV_",
            "FUNC_", "MI_", "INSTANCEID", "SERIALNUMBER"
        })
            if (raw.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;

        for (var i = 0; i + 2 < raw.Length; i++)
            if (char.IsAsciiLetter(raw[i]) && raw[i + 1] == ':' &&
                raw[i + 2] == '/')
                return true;
        return false;
    }
}
