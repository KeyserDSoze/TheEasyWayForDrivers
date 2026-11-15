using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Read-only parser for Dell's officially published deployment driver-pack
/// XML. Model names and published OS labels are NOT proof of a per-device
/// driver update, applicability, signature or installation eligibility.
/// </summary>
public static class DellDriverPackCatalog
{
    public const string OfficialCatalogUrl =
        "https://downloads.dell.com/catalog/DriverPackCatalog.cab";
    public const int MaximumMatchedPacks = 100;

    public sealed record Pack(
        string Model,
        string PackagePath,
        string Release,
        string OperatingSystems);

    public static IReadOnlyList<Pack> FindForModel(
        Stream xmlStream, string? model)
    {
        ArgumentNullException.ThrowIfNull(xmlStream);

        if (string.IsNullOrWhiteSpace(model) || model.Length > 160)
            return [];

        using var reader = XmlReader.Create(xmlStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 16 * 1024 * 1024,
            MaxCharactersFromEntities = 0
        });
        var catalog = XDocument.Load(reader, LoadOptions.None);
        var root = catalog.Root ??
            throw new InvalidDataException("Dell Driver Pack catalog XML is empty.");
        if (!string.Equals(root.Name.LocalName,
                "DriverPackManifest", StringComparison.Ordinal))
            throw new InvalidDataException(
                "Unexpected Dell Driver Pack catalog root.");

        var name = model.Trim();
        var packs = root.Elements()
            .Where(element => element.Name.LocalName == "DriverPackage" &&
                !string.Equals((string?)element.Attribute("type"),
                    "WinPE", StringComparison.OrdinalIgnoreCase))
            .SelectMany(package =>
            {
                var path = (string?)package.Attribute("path") ?? "";
                // Never treat a link, URL, traversal or malformed filename
                // from XML as an official relative Dell deployment path.
                if (!IsSafePackagePath(path))
                    return Enumerable.Empty<Pack>();

                var matches = package.Descendants()
                    .Where(node => node.Name.LocalName == "Model")
                    .Select(node => (string?)node.Attribute("name"))
                    .Where(value => string.Equals(value?.Trim(), name,
                        StringComparison.OrdinalIgnoreCase));

                // The OS collection can be unbounded even when the source
                // XML is under 16 MiB. Preserve a truthful unknown status
                // instead of truncating a potentially relevant OS list.
                var systemNames = package.Descendants()
                    .Where(node => node.Name.LocalName == "OperatingSystem")
                    .Select(node => (string?)node.Attribute("osCode") ??
                        $"{(string?)node.Attribute("majorVersion")}." +
                        $"{(string?)node.Attribute("minorVersion")} " +
                        $"({(string?)node.Attribute("osArch")})")
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => SafeDisplayText(value, 120,
                        "Sistema operativo da verificare"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(65).ToArray();

                var osList = systemNames.Length > 64
                    ? "Sistema operativo da verificare (elenco troppo lungo)"
                    : string.Join(", ", systemNames);
                if (osList.Length > 300 || osList.Length == 0)
                    osList = "Sistema operativo da verificare";

                var pack = ValidatePack(new Pack(name, path,
                    (string?)package.Attribute("dellVersion") ?? "N/D",
                    osList));

                return matches.Select(_ => pack);
            })
            .DistinctBy(pack => pack.PackagePath,
                StringComparer.OrdinalIgnoreCase)
            .Take(MaximumMatchedPacks + 1).ToArray();

        // An arbitrary 101st distinct pack must not disappear silently
        // while the UI calls the resulting list a verified full catalog.
        if (packs.Length > MaximumMatchedPacks)
            throw new InvalidDataException(
                "Troppi Driver Pack Dell per verificare il modello.");

        return packs;
    }

    /// <summary>
    /// The public Pack record can also come from a direct caller, old IPC
    /// snapshot or saved session. Apply the same boundaries at the UI
    /// and OEM-assessment entry points, not only during XML parsing.
    /// </summary>
    internal static Pack ValidatePack(Pack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        if (!IsSafePackagePath(pack.PackagePath))
            throw new InvalidDataException(
                "Identificativo Driver Pack Dell non valido.");

        return pack with
        {
            Model = SafeDisplayText(pack.Model, 160,
                "Modello da verificare"),
            Release = SafeDisplayText(pack.Release, 80, "N/D"),
            OperatingSystems = SafeDisplayText(pack.OperatingSystems,
                300, "Sistema operativo da verificare")
        };
    }

    private static bool IsSafePackagePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 512 ||
            path[0] == '/' || path[^1] == '/' ||
            path.Contains("..", StringComparison.Ordinal))
            return false;

        // Whitelist deliberately admits Dell's ordinary relative paths,
        // e.g. FOLDER/latitude.exe, not network paths, query fragments,
        // URI escapes, backslashes, whitespace or bidi/control markers.
        return path.All(ch => char.IsAsciiLetterOrDigit(ch) ||
            ch is '/' or '.' or '-' or '_') &&
            path.Split('/').All(segment =>
                segment.Length > 0 && segment is not "." and not "..");
    }

    private static string SafeDisplayText(
        string? raw, int maximum, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        // Check the *full* value before any preview limit; a PnP instance,
        // serial, email or local path can be hidden after 150 characters.
        if (ContainsPrivateReference(raw))
            return "[dato riservato]";
        if (raw.Length > maximum)
            return fallback; // never claim an incomplete OS list is complete

        var safe = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            var category = char.GetUnicodeCategory(ch);
            safe.Append(char.IsWhiteSpace(ch) || char.IsControl(ch) ||
                char.IsSurrogate(ch) ||
                category is UnicodeCategory.Format or
                    UnicodeCategory.LineSeparator or
                    UnicodeCategory.ParagraphSeparator
                ? ' ' : ch);
        }
        var result = safe.ToString().Trim();
        return result.Length > 0 ? result : fallback;
    }

    private static bool ContainsPrivateReference(string raw)
    {
        if (raw.Contains('\\') || raw.Contains('@') ||
            raw.Contains("://", StringComparison.Ordinal) ||
            raw.Contains("PRIVATE_", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith('/') ||
            raw.Contains("/Users/", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("/home/", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("/mnt/", StringComparison.OrdinalIgnoreCase))
            return true;

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
