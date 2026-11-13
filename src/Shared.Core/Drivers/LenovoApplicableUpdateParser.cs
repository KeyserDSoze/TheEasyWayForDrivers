using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Reads the local Lenovo.Client.Update Get-LnvUpdate result for the current
/// computer. These are Lenovo-assessed *needed* driver packages, not driver
/// installations approved or revalidated by OmegaDrive.
/// </summary>
public static class LenovoApplicableUpdateParser
{
    public const string OfficialSupportUrl = "https://support.lenovo.com/";
    public const int MaximumDriverResults = 80;
    // The opt-in Lenovo.Client.Update PowerShell reader aborts at 1001
    // applicable driver rows. IPC/legacy callers must not bypass the same
    // completeness gate and silently present just 80 as the full result.
    public const int MaximumReceivedRows = 1000;

    public sealed record Package(
        string PackageId,
        string Title,
        string Version,
        string ReleaseDate,
        string Severity);

    public static IReadOnlyList<Package> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        if (json.Length > 1024 * 1024)
            throw new InvalidDataException("Lenovo update response exceeds the size limit.");

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Null)
            return [];

        var entries = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().ToArray(),
            JsonValueKind.Object => [root],
            _ => throw new InvalidDataException("Unexpected Lenovo update response shape.")
        };

        if (entries.Length > MaximumReceivedRows)
            throw new InvalidDataException(
                "Lenovo update response contains too many entries.");

        var found = new List<(Package Entry, string Category)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object || !IsDriver(entry) ||
                !IsBoolean(entry, "IsApplicable", true) ||
                !IsBoolean(entry, "IsInstalled", false))
                continue;

            // PackageID is an opaque identity, not a human label. Never
            // "repair" a truncated token, private path or control character
            // into an apparently valid Lenovo package identity.
            var id = SafePackageId(entry);
            var title = Field(entry, "Title");
            if (id.Length == 0 || title.Length == 0 || !seen.Add(id))
                continue;

            found.Add((new Package(
                id, title,
                Fallback(Field(entry, "Version")),
                Fallback(Field(entry, "ReleaseDate")),
                Fallback(Field(entry, "Severity"))),
                Field(entry, "Category")));
        }

        return OemCatalogResultSelection.FairByCategory(
            found, MaximumDriverResults);
    }

    public static IReadOnlyList<VendorSourceCheck> ToVendorChecks(
        IEnumerable<Package> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        return packages.Select(package => new VendorSourceCheck(
            package.Title,
            "Lenovo",
            "Non confrontata",
            OfficialSupportUrl,
            "Lenovo.Client.Update · Get-LnvUpdate",
            $"ID pacchetto: {package.PackageId}; rilascio: {package.ReleaseDate}; " +
            $"priorità Lenovo: {package.Severity}",
            0,
            IsCatalogMetadata: true,
            PublishedVersion: package.Version,
            IsVendorApplicable: true)).ToArray();
    }

    private static bool IsDriver(JsonElement entry)
    {
        // Type outranks Category, including an explicitly malformed Type.
        // No CR/LF/TAB or invisible Unicode may be stripped to turn
        // "Driver\n" into official Lenovo Type=Driver metadata.
        if (entry.TryGetProperty("Type", out var type) &&
            type.ValueKind != JsonValueKind.Null)
            return IsExactDriverDiscriminator(type, allowNumericTwo: true);

        return entry.TryGetProperty("Category", out var category) &&
            IsExactDriverDiscriminator(category, allowNumericTwo: false);
    }

    private static bool IsExactDriverDiscriminator(
        JsonElement value, bool allowNumericTwo)
    {
        if (value.ValueKind != JsonValueKind.String &&
            !(allowNumericTwo && value.ValueKind == JsonValueKind.Number))
            return false;

        var raw = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : value.ToString();
        // Allow only harmless ASCII-space padding. Trim() would also
        // erase TAB/newline/nonbreaking Unicode from untrusted fields.
        var clean = raw.Trim(' ');
        return clean.Equals("Driver", StringComparison.OrdinalIgnoreCase) ||
            (allowNumericTwo && clean == "2");
    }

    private static bool IsBoolean(JsonElement entry, string name, bool expected) =>
        entry.TryGetProperty(name, out var value) &&
        value.ValueKind == (expected ? JsonValueKind.True : JsonValueKind.False);

    private static string Fallback(string value) =>
        value.Length == 0 ? "N/D" : value;

    private static string SafePackageId(JsonElement entry)
    {
        if (!entry.TryGetProperty("PackageID", out var value) ||
            value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
            return string.Empty;

        var raw = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : value.ToString();
        var id = raw.Trim(' ');
        // Do not expose a locally sourced path, PnP instance identifier,
        // URL, bidi/Unicode, or malformed/truncated token in vendor evidence.
        // Lenovo's opaque package keys can be numeric or alphanumeric.
        return id.Length is > 0 and <= 160 &&
            id.All(ch => char.IsAsciiLetterOrDigit(ch) ||
                ch is '-' or '_' or '.')
                ? id : string.Empty;
    }

    private static string Field(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
            return string.Empty;

        var raw = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : value.ToString();

        // Display-only metadata may contain Unicode words, but never
        // invisible controls, bidi overrides or malformed surrogates.
        // Preserve the 160-character UI budget without changing identity.
        var safe = new StringBuilder(Math.Min(raw.Length, 160));
        foreach (var ch in raw)
        {
            var category = char.GetUnicodeCategory(ch);
            safe.Append(char.IsControl(ch) || char.IsSurrogate(ch) ||
                category is UnicodeCategory.Format or
                    UnicodeCategory.LineSeparator or
                    UnicodeCategory.ParagraphSeparator
                ? ' ' : ch);
            if (safe.Length == 160)
                break;
        }
        return safe.ToString().Trim();
    }
}
