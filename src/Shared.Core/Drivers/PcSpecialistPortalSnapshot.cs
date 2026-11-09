using System.Net;
using System.Text.RegularExpressions;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Reads only the visible package names from a user-saved PCSpecialist
/// driver-list HTML page. No network, credentials, order number, URLs,
/// signed storage tokens, scripts or actual driver packages are consumed.
/// A saved HTML file does NOT authenticate its origin or prove that any
/// listed package is newer or applicable to this Windows device.
/// </summary>
public static class PcSpecialistPortalSnapshot
{
    public const int MaximumHtmlCharacters = 1024 * 1024;
    public const int MaximumLinks = 2048;
    public const int MaximumDisplayedPackages = 96;

    private static readonly Regex Anchor = new(
        @"<a\b(?<attributes>[^>]{0,2048})>(?<label>.{1,512}?)</a\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant |
        RegexOptions.Singleline | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(350));
    private static readonly Regex Href = new(
        """\bhref\s*=\s*["'](?<url>[^"']{1,2048})["']""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant |
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(350));
    private static readonly Regex Tags = new(
        @"<[^>]{1,160}>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(350));
    private static readonly Regex StorageHost = new(
        @"\Adrivers\.[0-9a-f]{32}\.r2\.cloudflarestorage\.com\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant |
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    public sealed record Package(string Name, string Category, string Type)
    {
        public bool IsInstallable => false;
        public bool IsHardwareMatched => false;
        public bool IsVerifiedOfficial => false;
        public string Detail =>
            $"{Category} · {Type}. Solo elenco HTML salvato: " +
            "firma, versione, Windows, origine e applicabilità NON verificate.";
    }

    public sealed record Report(
        IReadOnlyList<Package> Packages,
        int TotalDistinct,
        int Omitted)
    {
        public bool IsInstallable => false;
        public bool IsOfficialSourceVerified => false;
        public bool IsHardwareMatched => false;
        public bool IsPartial => Omitted > 0;
        public string Summary =>
            $"Elenco PCSpecialist da HTML locale: {TotalDistinct} nomi " +
            $"di pacchetti ZIP/CAB/EXE/MSI/7z, {Packages.Count} mostrati, " +
            $"{Omitted} omessi per limite. " +
            "La pagina salvata NON prova il proprio ordine/provenienza. " +
            "Nessun link, numero ordine o token trasmesso; " +
            "nessun download o installer eseguito. " +
            "NON sono aggiornamenti WUA o driver compatibili verificati. " +
            (IsPartial ? "Elenco PARZIALE: nessuna conclusione sui file omessi. " : "") +
            "Per attribuire un driver a un dispositivo, scaricare " +
            "manualmente dal portale ufficiale e confrontare gli INF locali.";
    }

    public static Report Parse(string html, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        cancellationToken.ThrowIfCancellationRequested();

        if (html.Length == 0 || html.Length > MaximumHtmlCharacters ||
            !html.Contains("Download Drivers / Manuals",
                StringComparison.OrdinalIgnoreCase) ||
            !html.Contains("Date Uploaded", StringComparison.OrdinalIgnoreCase) ||
            !html.Contains("File Size", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Il file HTML non è un elenco driver PCSpecialist riconosciuto " +
                "oppure supera il limite di 1 MiB.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packages = new List<Package>();
        var total = 0;
        var anchors = Anchor.Matches(html);
        if (anchors.Count > MaximumLinks)
            throw new InvalidDataException(
                "Troppi link nella pagina: elenco PCSpecialist non verificato.");

        foreach (Match anchor in anchors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var href = Href.Match(anchor.Groups["attributes"].Value);
            if (!href.Success ||
                !IsAllowedDownloadUrl(WebUtility.HtmlDecode(href.Groups["url"].Value)))
                continue;

            // Only text inside the download anchor is used; all href/query
            // parameters including time-limited signed S3 tokens are dropped.
            var visible = Tags.Replace(anchor.Groups["label"].Value, string.Empty);
            var name = WebUtility.HtmlDecode(visible).Trim();
            if (!IsSafePackageName(name) || !names.Add(name))
                continue;

            total++;
            if (packages.Count < MaximumDisplayedPackages)
            {
                var (category, type) = Categorize(name);
                packages.Add(new Package(name, category, type));
            }
        }

        if (total == 0)
            throw new InvalidDataException(
                "Nessun nome di pacchetto driver leggibile dalla pagina. " +
                "Il file HTML potrebbe essere incompleto o non essere " +
                "la lista dei download del portale ufficiale.");

        return new Report(packages
            .OrderBy(package => package.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray(), total, total - packages.Count);
    }

    public static bool IsAllowedDownloadUrl(string? href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        return uri.IdnHost.Equals("www.pcspecialist.co.uk",
                   StringComparison.OrdinalIgnoreCase) ||
               uri.IdnHost.Equals("pcspecialist.co.uk",
                   StringComparison.OrdinalIgnoreCase) ||
               StorageHost.IsMatch(uri.IdnHost);
    }

    private static bool IsSafePackageName(string name)
    {
        if (name.Length is < 6 or > 100 ||
            name.Any(ch => !(char.IsAsciiLetterOrDigit(ch) ||
                ch is ' ' or '-' or '_' or '.' or '(' or ')' or '&' or '+')) ||
            name.Contains("..", StringComparison.Ordinal) ||
            name.Contains("order", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("serial", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("invoice", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("token", StringComparison.OrdinalIgnoreCase))
            return false;

        return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".cab", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Category, string Type) Categorize(string name)
    {
        bool has(string s) => name.Contains(s, StringComparison.OrdinalIgnoreCase);

        // A file name is a category label only. Never correlate a package
        // with an actual PnP device or infer an installable update.
        if (has("Control Centre") || has("Control Center") ||
            has("Apps.") || has("Nahimic"))
            return ("Software OEM", "Applicazione / componente opzionale");
        if (has("Nvidia") && (has("VGA") || has("GPU") || has("Graphics")))
            return ("NVIDIA · grafica", "Pacchetto driver da verificare");
        if (has("Intel") && (has("VGA") || has("Graphics")))
            return ("Intel · grafica", "Pacchetto driver da verificare");
        if (has("Bluetooth"))
            return ("Bluetooth", "Pacchetto driver da verificare");
        if (has("WiFi") || has("Wi-Fi") || has("WLAN") || has("Killer"))
            return ("Wi-Fi / Killer", "Pacchetto driver da verificare");
        if (has("Realtek") && has("Audio"))
            return ("Realtek · audio", "Pacchetto driver da verificare");
        if (has("Realtek") && (has("LAN") || has("Ethernet")))
            return ("Realtek · rete", "Pacchetto driver da verificare");
        if (has("Intel ME") || has("Firmware") || has("BIOS"))
            return ("Piattaforma / firmware", "Verifica manuale necessaria");
        if (has("Intel") || has("Chipset") || has("Thunderbolt") ||
            has("Cardreader") || has("Card Reader"))
            return ("Chipset / periferiche", "Pacchetto da verificare");
        return ("Altri pacchetti OEM", "Tipo non verificato");
    }
}
