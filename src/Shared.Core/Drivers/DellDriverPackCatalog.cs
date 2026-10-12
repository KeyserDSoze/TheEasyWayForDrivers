using System.Xml;
using System.Xml.Linq;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Read-only parser for Dell's officially published deployment driver pack XML.
/// Pack applicability is model-name based only; Windows release and exact
/// system/BIOS identifier must still be checked by the user.
/// </summary>
public static class DellDriverPackCatalog
{
    public const string OfficialCatalogUrl =
        "https://downloads.dell.com/catalog/DriverPackCatalog.cab";

    public sealed record Pack(
        string Model,
        string PackagePath,
        string Release,
        string OperatingSystems);

    public static IReadOnlyList<Pack> FindForModel(
        Stream xmlStream,
        string? model)
    {
        ArgumentNullException.ThrowIfNull(xmlStream);

        if (string.IsNullOrWhiteSpace(model) || model.Length > 160)
            return [];

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 16 * 1024 * 1024,
            MaxCharactersFromEntities = 0
        };

        using var reader = XmlReader.Create(xmlStream, settings);
        var catalog = XDocument.Load(reader, LoadOptions.None);
        var root = catalog.Root
            ?? throw new InvalidDataException("Dell Driver Pack catalog XML is empty.");
        if (!string.Equals(root.Name.LocalName,
                "DriverPackManifest", StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected Dell Driver Pack catalog root.");

        var name = model.Trim();
        return root.Elements()
            .Where(element => element.Name.LocalName == "DriverPackage" &&
                !string.Equals((string?)element.Attribute("type"), "WinPE",
                    StringComparison.OrdinalIgnoreCase))
            .SelectMany(package =>
            {
                var path = (string?)package.Attribute("path") ?? string.Empty;
                // Metadata only. Never execute or download arbitrary paths.
                if (path.Length is < 1 or > 512 || path.StartsWith('/') ||
                    path.Contains("..", StringComparison.Ordinal) ||
                    path.Contains(':') || path.Contains('\\'))
                    return Enumerable.Empty<Pack>();

                var systems = package.Descendants()
                    .Where(node => node.Name.LocalName == "Model")
                    .Select(node => (string?)node.Attribute("name"))
                    .Where(value => string.Equals(value?.Trim(), name,
                        StringComparison.OrdinalIgnoreCase));

                var os = string.Join(", ", package.Descendants()
                    .Where(node => node.Name.LocalName == "OperatingSystem")
                    .Select(node => (string?)node.Attribute("osCode") ??
                        $"{(string?)node.Attribute("majorVersion")}.{(string?)node.Attribute("minorVersion")} ({(string?)node.Attribute("osArch")})")
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase));

                return systems.Select(_ => new Pack(
                    name,
                    path,
                    (string?)package.Attribute("dellVersion") ?? "N/D",
                    os.Length == 0 ? "Sistema operativo da verificare" : os));
            })
            .DistinctBy(pack => pack.PackagePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
