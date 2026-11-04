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

            var id = Field(softpaq, "Id");
            var name = Field(softpaq, "Name");
            if (!Regex.IsMatch(id, @"\Asp\d{4,9}\z", RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant) || name.Length == 0)
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

        return packages.Select(package => new VendorSourceCheck(
            package.Title,
            "HP",
            package.InstalledVersion,
            OfficialSupportUrl,
            "HP Image Assistant · Analyze / Drivers / List",
            $"SoftPaq {package.SoftPaqId} · {package.HpComment}",
            0,
            IsCatalogMetadata: true,
            PublishedVersion: package.AvailableVersion,
            IsVendorApplicable: true)).ToArray();
    }

    private static string Missing(string value) =>
        value.Length == 0 ? "N/D" : value;

    private static XElement? Child(XElement? element, string name) =>
        element?.Elements().FirstOrDefault(child =>
            child.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string Field(XElement? parent, string name)
    {
        var element = Child(parent, name);
        if (element is null || element.HasElements)
            return string.Empty;

        var clean = element.Value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length > 160 ? clean[..160] : clean;
    }
}
