using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Interprets fields published by the Lenovo Client Scripting module.
/// Metadata only: no execution of descriptors, packages, or firmware.
/// </summary>
public static class LenovoCatalogParser
{
    public const int MaximumDriverResults = 50;

    // Desktop forwards at most 1000 PackageType=2 records plus a sentinel.
    // Older IPC and direct callers enforce this BEFORE filtering and dedup.
    public const int MaximumReceivedRows = 1000;

    public sealed record Package(string Id, string Name, string Version,
        string Category, string ReleaseDate);

    public static IReadOnlyList<Package> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        if (json.Length > 1024 * 1024)
            throw new InvalidDataException("Lenovo metadata too large.");

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
            _ => throw new InvalidDataException("Invalid Lenovo response JSON shape.")
        };
        if (entries.Length > MaximumReceivedRows)
            throw new InvalidDataException(
                "Lenovo catalog exceeds the verifiable response row limit.");

        var results = new List<(Package Entry, string Category)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !IsDriverPackageType(entry))
                continue;

            // Package IDs are identities, not labels. Never normalize TAB,
            // newlines, bidi or an overlong ID into a different valid ID
            // before deduplicating. No package becomes installable here.
            var id = SafePackageId(RawValue(entry, "ID"));
            var name = SafeDisplayText(RawValue(entry, "Name"));
            if (id.Length == 0 || name.Length == 0 || !seen.Add(id))
                continue;

            var category = SafeDisplayText(RawValue(entry, "Category"));
            results.Add((new Package(id, name,
                SafeDisplayText(RawValue(entry, "Version")), category,
                SafeDisplayText(RawValue(entry, "ReleaseDate"))), category));
        }

        return OemCatalogResultSelection.FairByCategory(
            results, MaximumDriverResults);
    }

    /// <summary>
    /// The public Package record also reaches Discovery+ through direct
    /// calls and saved/session data, not only through Parse. Revalidate
    /// identity and display privacy at the final presentation boundary.
    /// </summary>
    internal static Package ValidatePackage(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var id = SafePackageId(package.Id);
        var name = SafeDisplayText(package.Name);
        if (id.Length == 0 || name.Length == 0)
            throw new InvalidDataException(
                "Invalid Lenovo catalog package identity.");

        return package with
        {
            Id = id,
            Name = name,
            Version = SafeDisplayText(package.Version),
            Category = SafeDisplayText(package.Category),
            ReleaseDate = SafeDisplayText(package.ReleaseDate)
        };
    }

    private static bool IsDriverPackageType(JsonElement entry)
    {
        if (!entry.TryGetProperty("PackageType", out var type) ||
            type.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
            return false;
        // Only the official code 2. Trim ASCII SPACE padding, not all
        // Unicode whitespace or controls: "2\n" is not Lenovo evidence.
        var raw = type.ValueKind == JsonValueKind.String
            ? type.GetString() ?? "" : type.ToString();
        return raw.Trim(' ') == "2";
    }

    private static string? RawValue(JsonElement entry, string field)
    {
        if (!entry.TryGetProperty(field, out var value) ||
            value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
            return null;
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() : value.ToString();
    }

    private static string SafePackageId(string? raw)
    {
        // Strict ASCII key: no truncation, path/serial repair, Unicode
        // digit or invisible character can turn into a valid package ID.
        var id = raw?.Trim(' ') ?? "";
        return id.Length is > 0 and <= 160 &&
            id.All(ch => char.IsAsciiLetterOrDigit(ch) ||
                ch is '-' or '_' or '.')
            ? id : "";
    }

    private static string SafeDisplayText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        // Inspect the FULL original value before limiting visible text;
        // a private Windows instance or local path may occur after char 160.
        if (ContainsPrivateReference(raw))
            return "[dato riservato]";

        var safe = new StringBuilder(Math.Min(raw.Length, 160));
        foreach (var ch in raw)
        {
            var category = char.GetUnicodeCategory(ch);
            safe.Append(char.IsWhiteSpace(ch) || char.IsControl(ch) ||
                char.IsSurrogate(ch) || category is
                    UnicodeCategory.Format or UnicodeCategory.LineSeparator or
                    UnicodeCategory.ParagraphSeparator
                ? ' ' : ch);
            if (safe.Length == 160)
                break;
        }
        return safe.ToString().Trim();
    }

    private static bool ContainsPrivateReference(string raw)
    {
        // Read-only MachineType metadata is not a trustworthy source of
        // local PnP instance IDs, user paths, emails, or URL fragments.
        if (raw.Contains('\\') || raw.Contains('@') ||
            raw.Contains("://", StringComparison.Ordinal) ||
            raw.Contains("PRIVATE_", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var marker in new[]
        {
            "VEN_", "VID_", "DEV_", "PID_", "SUBSYS_", "REV_",
            "INSTANCEID", "SERIALNUMBER"
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
