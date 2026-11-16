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

    // These fields determine whether Lenovo considers a package a needed,
    // uninstalled driver, and which package is shown to the user. Treat
    // duplicate JSON keys (including case variants) as untrusted input.
    private static readonly HashSet<string> CriticalMetadataKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Type", "Category", "IsApplicable", "IsInstalled",
            "PackageID", "Title", "Version", "ReleaseDate", "Severity"
        };

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
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                OemJsonMetadataIntegrity.HasDuplicateCriticalKeys(
                    entry, CriticalMetadataKeys) ||
                !IsDriver(entry) ||
                !IsBoolean(entry, "IsApplicable", true) ||
                !IsBoolean(entry, "IsInstalled", false))
                continue;

            // PackageID is an opaque identity, not a human label. Never
            // "repair" a truncated token, private path or control character
            // into an apparently valid Lenovo package identity.
            var id = SafePackageId(entry);
            var title = Field(entry, "Title");
            if (id.Length == 0 || title.Length == 0)
                continue;

            var candidate = new Package(
                id, title,
                Fallback(Field(entry, "Version")),
                Fallback(Field(entry, "ReleaseDate")),
                Fallback(Field(entry, "Severity")));
            var category = Field(entry, "Category");
            if (seen.TryGetValue(id, out var priorIndex))
            {
                // Keep current-PC IsApplicable/IsInstalled checks ABOVE
                // this point. A masked title in an earlier eligible row
                // must not hide a later readable eligible driver package.
                if (OemCatalogResultSelection.IsRedactedTitle(
                        found[priorIndex].Entry.Title) &&
                    !OemCatalogResultSelection.IsRedactedTitle(candidate.Title))
                    found[priorIndex] = (candidate, category);
                continue;
            }
            seen.Add(id, found.Count);
            found.Add((candidate, category));
        }

        return OemCatalogResultSelection.FairByCategory(
            found, MaximumDriverResults);
    }

    public static IReadOnlyList<VendorSourceCheck> ToVendorChecks(
        IEnumerable<Package> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        return packages.Select(raw =>
        {
            // #458: Package is public. Older session data and direct callers
            // can bypass Parse, including its checks on IDs and display
            // fields sourced from this current-PC report. Revalidate HERE
            // before publishing anything to UI or copied OEM evidence.
            var package = ValidatePackage(raw);
            return new VendorSourceCheck(
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
                IsVendorApplicable: true);
        }).ToArray();
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
        return SafePackageId(id);
    }

    // Reject identity strings as a whole rather than truncating/recovering
    // them. Used on BOTH parsed JSON and directly supplied Package records.
    private static string SafePackageId(string? raw)
    {
        var id = raw?.Trim(' ') ?? string.Empty;
        return id.Length is > 0 and <= 160 &&
            id.All(ch => char.IsAsciiLetterOrDigit(ch) ||
                ch is '-' or '_' or '.')
            ? id : string.Empty;
    }

    private static Package ValidatePackage(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var id = SafePackageId(package.PackageId);
        var title = SafeDisplayText(package.Title);
        if (id.Length == 0 || title.Length == 0)
            throw new InvalidDataException(
                "Lenovo current-PC package identity is not verifiable.");

        return package with
        {
            PackageId = id,
            Title = title,
            Version = Fallback(SafeDisplayText(package.Version)),
            ReleaseDate = Fallback(SafeDisplayText(package.ReleaseDate)),
            Severity = Fallback(SafeDisplayText(package.Severity))
        };
    }

    private static string Field(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
            return string.Empty;

        var raw = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : value.ToString();

        return SafeDisplayText(raw);
    }

    private static string SafeDisplayText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        // A Lenovo.Client.Update result describes the CURRENT machine.
        // Examine the entire raw field before the 160-char UI preview;
        // a private PnP ID, username, serial or path may be in its tail.
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
        if (raw.Contains('\\') || raw.Contains('@') ||
            raw.Contains("://", StringComparison.Ordinal) ||
            raw.Contains("PRIVATE_", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith('/') ||
            raw.Contains("/Users/", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("/home/", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("/mnt/", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("/var/", StringComparison.OrdinalIgnoreCase))
            return true;

        // Hardware model IDs and instance attributes are not normal
        // display metadata and must never be copied to diagnostic text.
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
