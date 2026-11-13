using System.Xml;
using System.Xml.Linq;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Reads a Dell Command | Update /scan -report XML file. DCU assesses the
/// current machine. We require an explicit driver type and never infer an
/// installable driver from an unrecognised report schema.
/// </summary>
public static class DellApplicableUpdateParser
{
    public const string OfficialSupportUrl = "https://www.dell.com/support/home/";

    public sealed record Package(
        string Id,
        string Title,
        string Version,
        string Severity);

    public static IReadOnlyList<Package> Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new InvalidDataException("Empty Dell scan report.");

        if (xml.Length > 1024 * 1024)
            throw new InvalidDataException("Dell scan report exceeds the size limit.");

        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1024 * 1024,
            MaxCharactersFromEntities = 0
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        if (document.Root is null)
            throw new InvalidDataException("Dell scan report has no root.");

        // The report can contain other information and nested collections.
        // Only recognise an explicit list of updates, never arbitrary XML.
        var rootName = document.Root.Name.LocalName;
        if (rootName is not ("Updates" or "UpdateReport" or
            "UpdatesReport" or "DCUReport" or "Results" or "ScanReport"))
            throw new InvalidDataException("Unrecognised Dell scan report schema.");

        // The XML document is already bounded to 1 MiB, so inspect every
        // Update node rather than truncating the first 151 BEFORE checking
        // Type. Real DCU reports can list many BIOS/firmware/application
        // records ahead of NVIDIA/Killer driver entries.
        var updates = document.Root.Descendants()
            .Where(element => element.Name.LocalName == "Update");

        var output = new List<Package>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in updates)
        {
            // A discriminator is not display text: never "repair" a
            // newline, TAB or bidi control into an official Driver label.
            var type = RawValue(element, "Type", "UpdateType")?.Trim(' ');
            if (string.IsNullOrEmpty(type))
                throw new InvalidDataException(
                    "Dell scan report update type missing; unable to verify driver-only filtering.");

            if (!type.Equals("Driver", StringComparison.OrdinalIgnoreCase))
                continue;

            // An opaque component identity must never be truncated like a
            // title: distinct packages could collapse to the same 160
            // characters, and a private instance/path could reach the UI.
            var id = SafeComponentId(RawValue(element,
                "Id", "ID", "UpdateID", "ComponentID", "PackageID"));
            var name = SafeDisplayText(RawValue(element, "Name", "Title"));
            if (id.Length == 0 || name.Length == 0)
                throw new InvalidDataException(
                    "Dell scan report entry missing package identity or title.");

            if (!seen.Add(id))
                continue;

            // Bound unique, validated DRIVER packages, not unrelated
            // BIOS/firmware/application records. Never silently discard a
            // 151st distinct driver and misreport the scan as complete.
            if (output.Count == 150)
                throw new InvalidDataException(
                    "Dell scan report has too many driver entries.");

            var version = SafeDisplayText(RawValue(element,
                "Version", "ReleaseVersion"));
            var severity = SafeDisplayText(RawValue(element,
                "Severity", "UpdateSeverity"));
            output.Add(new Package(id, name,
                version.Length == 0 ? "N/D" : version,
                severity.Length == 0 ? "N/D" : severity));
        }

        return output;
    }

    public static IReadOnlyList<VendorSourceCheck> ToVendorChecks(
        IEnumerable<Package> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        // Callers of the public Package record (including old IPC/session
        // data) can bypass Parse(). Revalidate both identity and privacy
        // at the final boundary to user-visible/copied OEM evidence.
        return packages.Select(package =>
        {
            var id = SafeComponentId(package.Id);
            var title = SafeDisplayText(package.Title);
            if (id.Length == 0 || title.Length == 0)
                throw new InvalidDataException(
                    "Invalid Dell Command Update recommendation identity.");

            return new VendorSourceCheck(
                title,
                "Dell",
                "Non confrontata",
                OfficialSupportUrl,
                "Dell Command Update · scansione per PC corrente",
                $"ID componente: {id}; severità Dell: " +
                    Missing(SafeDisplayText(package.Severity)),
                0,
                IsCatalogMetadata: true,
                PublishedVersion: Missing(SafeDisplayText(package.Version)),
                IsVendorApplicable: true);
        }).ToArray();
    }

    private static string Missing(string text) =>
        text.Length == 0 ? "N/D" : text;

    private static string? RawValue(XElement element, params string[] names)
    {
        // Dell DCU schemas vary: only direct attributes or child text can
        // attest Type/ID. Do not trust concatenated text from nested nodes.
        foreach (var name in names)
        {
            var attribute = element.Attributes().FirstOrDefault(a =>
                a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (attribute is not null && !string.IsNullOrWhiteSpace(attribute.Value))
                return attribute.Value;

            var child = element.Elements().FirstOrDefault(e =>
                e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (child is null)
                continue;
            if (child.HasElements)
                throw new InvalidDataException(
                    "Dell scan report contains a nested update field.");
            if (!string.IsNullOrWhiteSpace(child.Value))
                return child.Value;
        }
        return null;
    }

    private static string SafeComponentId(string? value)
    {
        // Whitespace normalization and truncation are forbidden for
        // identity; only harmless ASCII-space padding is accepted.
        var id = value?.Trim(' ') ?? string.Empty;
        if (id.Length is < 1 or > 160)
            return string.Empty;

        // Dell can use canonical brace-delimited GUID component IDs.
        if (id.StartsWith('{') || id.EndsWith('}'))
            return Guid.TryParseExact(id, "B", out _) ? id : string.Empty;

        return id.All(ch => char.IsAsciiLetterOrDigit(ch) ||
            ch is '.' or '_' or '-')
            ? id : string.Empty;
    }

    private static string SafeDisplayText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        // Check the entire local current-PC report field BEFORE the
        // 160-character visible preview. A later private PnP instance
        // must not be hidden behind an innocent-looking long prefix.
        if (ContainsPrivateReference(value))
            return "[dato riservato]";

        var result = new System.Text.StringBuilder(Math.Min(value.Length, 160));
        foreach (var ch in value)
        {
            var category = char.GetUnicodeCategory(ch);
            result.Append(char.IsWhiteSpace(ch) || char.IsControl(ch) ||
                char.IsSurrogate(ch) ||
                category is System.Globalization.UnicodeCategory.Format or
                    System.Globalization.UnicodeCategory.LineSeparator or
                    System.Globalization.UnicodeCategory.ParagraphSeparator
                ? ' ' : ch);
            if (result.Length == 160)
                break;
        }
        return result.ToString().Trim();
    }

    private static bool ContainsPrivateReference(string value)
    {
        // Never copy local paths, URLs, emails or PnP/instance tokens from
        // DCU output; keep only an explicit privacy-safe placeholder.
        if (value.Contains('\\') || value.Contains('@') ||
            value.Contains("://", StringComparison.Ordinal) ||
            value.Contains("PRIVATE_", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var marker in new[]
        {
            "VEN_", "VID_", "DEV_", "PID_", "SUBSYS_", "REV_", "FUNC_",
            "INSTANCEID", "SERIALNUMBER"
        })
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;

        for (var i = 0; i + 2 < value.Length; i++)
            if (char.IsAsciiLetter(value[i]) && value[i + 1] == ':' &&
                value[i + 2] == '/')
                return true;

        return false;
    }
}
