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
            JsonValueKind.Array => root.EnumerateArray().Take(80).ToArray(),
            JsonValueKind.Object => [root],
            _ => throw new InvalidDataException("Unexpected Lenovo update response shape.")
        };

        var found = new List<Package>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object || !IsDriver(entry) ||
                !IsBoolean(entry, "IsApplicable", true) ||
                !IsBoolean(entry, "IsInstalled", false))
                continue;

            var id = Field(entry, "PackageID");
            var title = Field(entry, "Title");
            if (id.Length == 0 || title.Length == 0 || !seen.Add(id))
                continue;

            found.Add(new Package(
                id, title,
                Fallback(Field(entry, "Version")),
                Fallback(Field(entry, "ReleaseDate")),
                Fallback(Field(entry, "Severity"))));
        }

        return found;
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
        // Type is the authoritative driver/BIOS/firmware discriminator when
        // provided. Category alone can describe audio, graphics, etc.
        var kind = Field(entry, "Type");
        if (kind.Length > 0)
            return kind.Equals("Driver", StringComparison.OrdinalIgnoreCase) ||
                kind == "2";

        return Field(entry, "Category")
            .Equals("Driver", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBoolean(JsonElement entry, string name, bool expected) =>
        entry.TryGetProperty(name, out var value) &&
        value.ValueKind == (expected ? JsonValueKind.True : JsonValueKind.False);

    private static string Fallback(string value) =>
        value.Length == 0 ? "N/D" : value;

    private static string Field(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
            return string.Empty;

        var text = value.ToString().Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > 160 ? text[..160] : text;
    }
}
