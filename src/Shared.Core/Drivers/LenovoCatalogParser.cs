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

    // A last-key-wins PackageType=2 may otherwise disguise Firmware/BIOS.
    // Duplicate identities and display fields must not influence dedup
    // or the category-fair preview.
    private static readonly HashSet<string> CriticalMetadataKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "PackageType", "ID", "Name", "Version",
            "Category", "ReleaseDate"
        };

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
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                OemJsonMetadataIntegrity.HasDuplicateCriticalKeys(
                    entry, CriticalMetadataKeys) ||
                !IsDriverPackageType(entry))
                continue;

            // Package IDs are identities, not labels. Never normalize TAB,
            // newlines, bidi or an overlong ID into a different valid ID
            // before deduplicating. No package becomes installable here.
            var id = SafePackageId(RawValue(entry, "ID"));
            var name = SafeDisplayText(RawValue(entry, "Name"));
            if (id.Length == 0 || name.Length == 0)
                continue;

            var category = SafeDisplayText(RawValue(entry, "Category"));
            var candidate = new Package(id, name,
                SafeDisplayText(RawValue(entry, "Version")), category,
                SafeDisplayText(RawValue(entry, "ReleaseDate")));
            if (seen.TryGetValue(id, out var priorIndex))
            {
                // A private first title is not evidence worth keeping when
                // another row for the same valid package has a public name.
                // Keep first-seen provenance when both names are public.
                if (OemCatalogResultSelection.IsRedactedTitle(
                        results[priorIndex].Entry.Name) &&
                    !OemCatalogResultSelection.IsRedactedTitle(name))
                    results[priorIndex] = (candidate, category);
                continue;
            }

            seen.Add(id, results.Count);
            results.Add((candidate, category));
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

    // This identity and the displayed metadata can originate from a
    // saved MachineType catalog or direct IPC record. A syntactically
    // valid ASCII string is not necessarily a public Lenovo package ID:
    // user/serial/order markers must never become Discovery+ identifiers.
    private static readonly string[] PrivateMarkers =
    [
        "PRIVATE_", "SECRET_", "ORDER_", "CUSTOMER_", "SERIAL_",
        "ACCOUNT_", "USERNAME_", "HOSTNAME_", "PASSWORD_", "EMAIL_",
        "INSTANCEID", "SERIALNUMBER", "DEVICEID", "UUID_"
    ];

    private static readonly string[] PnpMarkers =
    [
        "VEN_", "VID_", "DEV_", "PID_", "SUBSYS_", "REV_",
        "FUNC_", "MI_"
    ];

    private static bool ContainsPrivateMarker(string value) =>
        PrivateMarkers.Any(marker =>
            value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsPnpMarker(string value) =>
        PnpMarkers.Any(marker =>
            value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string SafePackageId(string? raw)
    {
        // Strict ASCII key: no truncation, path/serial repair, Unicode
        // digit or invisible character can turn into a valid package ID.
        var id = raw?.Trim(' ') ?? "";
        return id.Length is > 0 and <= 160 &&
            id.All(ch => char.IsAsciiLetterOrDigit(ch) ||
                ch is '-' or '_' or '.') &&
            !ContainsPrivateMarker(id) && !ContainsPnpMarker(id)
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
        // Inspect the FULL original string before the 160-char display
        // limit. Remove invisible format/control characters ONLY in this
        // inspection copy: P\u200BRIVATE_ or OR\u2060DER_ must be treated
        // exactly like the underlying private marker. Ordinary readable
        // international text is still preserved in the visible label.
        var inspection = new string(raw.Where(ch =>
            !char.IsControl(ch) && !char.IsSurrogate(ch) &&
            char.GetUnicodeCategory(ch) is not (
                UnicodeCategory.Format or UnicodeCategory.LineSeparator or
                UnicodeCategory.ParagraphSeparator)).ToArray());

        // Read-only MachineType metadata is not a trustworthy source of
        // local PnP instance IDs, user paths, emails or URL fragments.
        if (inspection.Contains('\\') || inspection.Contains('@') ||
            inspection.Contains("://", StringComparison.Ordinal) ||
            inspection.StartsWith('/') ||
            inspection.Contains("/Users/", StringComparison.OrdinalIgnoreCase) ||
            inspection.Contains("/home/", StringComparison.OrdinalIgnoreCase) ||
            inspection.Contains("/mnt/", StringComparison.OrdinalIgnoreCase) ||
            ContainsPrivateMarker(inspection) ||
            ContainsPnpMarker(inspection))
            return true;

        for (var i = 0; i + 2 < inspection.Length; i++)
            if (char.IsAsciiLetter(inspection[i]) && inspection[i + 1] == ':' &&
                inspection[i + 2] == '/')
                return true;
        return false;
    }
}
