using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Extracts only the recommendation nodes from HP Image Assistant's current-PC
/// analysis report. HPIA owns the applicability decision; OmegaDrive does not
/// independently certify INF compatibility, versions or installability.
/// </summary>
public static class HpImageAssistantReportParser
{
    public const string OfficialSupportUrl = "https://support.hp.com/";
    private const int MaxReportChars = 4 * 1024 * 1024;

    public sealed record Package(
        string SoftPaqId,
        string Title,
        string InstalledVersion,
        string AvailableVersion,
        string HpComment);

    public static IReadOnlyList<Package> Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new InvalidDataException("HP Image Assistant report is missing.");
        if (xml.Length > MaxReportChars)
            throw new InvalidDataException("HP Image Assistant report exceeds the size limit.");

        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxReportChars,
            MaxCharactersFromEntities = 0
        });

        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root;
        if (root is null || root.Name.LocalName != "HPIA")
            throw new InvalidDataException("Unrecognized HP Image Assistant report root.");

        var recommendations = Child(root, "Recommendations");
        if (recommendations is null)
            throw new InvalidDataException(
                "HP Image Assistant report does not contain a Recommendations section.");

        // Deliberately do not read /Summary/Drivers counts or any of the
        // sibling Software, BIOS and Firmware recommendation collections.
        var drivers = Child(recommendations, "Drivers");
        if (drivers is null)
            return [];

        var entries = drivers.Elements().ToArray();
        if (entries.Length > 150)
            throw new InvalidDataException("Too many HP Image Assistant driver entries.");

        var results = new List<Package>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in entries)
        {
            if (item.Name.LocalName != "Recommendation")
                throw new InvalidDataException("Unexpected HPIA driver entry.");

            var softpaq = Child(Child(item, "Solution"), "Softpaq");
            if (softpaq is null)
                throw new InvalidDataException("HPIA driver recommendation has no SoftPaq.");

            // SoftPaq identity is NOT a display label: never strip TAB,
            // newline, bidi or Unicode digits into an apparent HP ID.
            var id = SafeSoftPaqId(Child(softpaq, "Id")?.Value);
            var name = Field(softpaq, "Name");
            if (id.Length == 0 || name.Length == 0)
                throw new InvalidDataException(
                    "HPIA driver recommendation has no valid SoftPaq identity.");

            if (!seen.Add(id))
                continue;

            var referenceVersion = Field(item, "ReferenceVersion");
            var packageVersion = Field(softpaq, "Version");
            results.Add(new Package(
                id, name,
                Missing(Field(item, "TargetVersion")),
                Missing(referenceVersion.Length > 0 ? referenceVersion : packageVersion),
                Missing(Field(item, "Comments"))));
        }

        return results;
    }

    public static IReadOnlyList<VendorSourceCheck> ToVendorChecks(
        IEnumerable<Package> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        // Package is public: older IPC or other direct callers can
        // construct it without going through Parse(). Revalidate at the
        // boundary to the user-visible OEM cards and copied details too.
        return packages.Select(package =>
        {
            var id = SafeSoftPaqId(package.SoftPaqId);
            var title = SafeDisplayText(package.Title);
            if (id.Length == 0 || title.Length == 0)
                throw new InvalidDataException(
                    "Invalid HP Image Assistant recommendation identity.");

            return new VendorSourceCheck(
                title,
                "HP",
                Missing(SafeDisplayText(package.InstalledVersion)),
                OfficialSupportUrl,
                "HP Image Assistant · Analyze / Drivers / List",
                $"SoftPaq {id} · {Missing(SafeDisplayText(package.HpComment))}",
                0,
                IsCatalogMetadata: true,
                PublishedVersion: Missing(SafeDisplayText(
                    package.AvailableVersion)),
                IsVendorApplicable: true);
        }).ToArray();
    }

    private static string Missing(string value) =>
        value.Length == 0 ? "N/D" : value;

    private static XElement? Child(XElement? element, string name) =>
        element?.Elements().FirstOrDefault(child =>
            child.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string Field(XElement? parent, string name)
    {
        var element = Child(parent, name);
        return element is null || element.HasElements
            ? string.Empty : SafeDisplayText(element.Value);
    }

    private static string SafeSoftPaqId(string? raw)
    {
        // Trim only harmless ASCII-space padding, never other whitespace.
        // In .NET, \\d also matches Unicode digits: use [0-9] explicitly.
        var value = raw?.Trim(' ') ?? string.Empty;
        return Regex.IsMatch(value, @"\Asp[0-9]{4,9}\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100))
            ? value : string.Empty;
    }

    private static string SafeDisplayText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        // Check ALL input before the 160-character UI cap. Otherwise a
        // long, benign prefix can hide a private PnP instance or path in
        // the tail of an HPIA Name/Comments/Version field.
        if (ContainsPrivateReference(raw))
            return "[dato riservato]";

        var clean = new System.Text.StringBuilder(Math.Min(raw.Length, 160));
        foreach (var ch in raw)
        {
            var category = char.GetUnicodeCategory(ch);
            clean.Append(char.IsWhiteSpace(ch) || char.IsControl(ch) ||
                char.IsSurrogate(ch) ||
                category is System.Globalization.UnicodeCategory.Format or
                    System.Globalization.UnicodeCategory.LineSeparator or
                    System.Globalization.UnicodeCategory.ParagraphSeparator
                ? ' ' : ch);
            if (clean.Length == 160)
                break;
        }
        return clean.ToString().Trim();
    }

    private static bool ContainsPrivateReference(string value)
    {
        // HPIA is a CURRENT-PC report, not public catalog metadata.
        // Never copy native instance IDs, local paths, URLs or email-like
        // text even when HP places them inside a free-form comment.
        if (value.Contains('\\') || value.Contains('@') ||
            value.Contains("://", StringComparison.Ordinal) ||
            value.Contains("PRIVATE_", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var marker in new[]
        {
            "VEN_", "VID_", "DEV_", "PID_", "SUBSYS_", "REV_", "FUNC_",
            "MI_", "INSTANCEID", "SERIALNUMBER"
        })
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;

        for (var i = 0; i + 2 < value.Length; i++)
            if (char.IsAsciiLetter(value[i]) && value[i + 1] == ':' &&
                value[i + 2] == '/')
                return true; // e.g. C:/Users/PRIVATE_PC/driver.inf

        return false;
    }
}
