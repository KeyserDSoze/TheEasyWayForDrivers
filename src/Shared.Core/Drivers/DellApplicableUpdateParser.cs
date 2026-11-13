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
            var type = Value(element, "Type", "UpdateType");
            if (type.Length == 0)
                throw new InvalidDataException(
                    "Dell scan report update type missing; unable to verify driver-only filtering.");

            if (!type.Equals("Driver", StringComparison.OrdinalIgnoreCase))
                continue;

            var id = Value(element, "Id", "ID", "UpdateID", "ComponentID", "PackageID");
            var name = Value(element, "Name", "Title");
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

            var version = Value(element, "Version", "ReleaseVersion");
            var severity = Value(element, "Severity", "UpdateSeverity");
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

        return packages.Select(package => new VendorSourceCheck(
            package.Title,
            "Dell",
            "Non confrontata",
            OfficialSupportUrl,
            "Dell Command Update · scansione per PC corrente",
            $"ID componente: {package.Id}; severità Dell: {package.Severity}",
            0,
            IsCatalogMetadata: true,
            PublishedVersion: package.Version,
            IsVendorApplicable: true)).ToArray();
    }

    private static string Value(XElement element, params string[] names)
    {
        // Dell report formats vary across DCU versions; accept only the
        // immediate attributes or child fields for explicit update entries.
        foreach (var name in names)
        {
            var value = element.Attributes()
                .FirstOrDefault(a => a.Name.LocalName.Equals(
                    name, StringComparison.OrdinalIgnoreCase))?.Value
                ?? element.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals(
                    name, StringComparison.OrdinalIgnoreCase))?.Value;
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length > 160 ? clean[..160] : clean;
        }
        return "";
    }
}
