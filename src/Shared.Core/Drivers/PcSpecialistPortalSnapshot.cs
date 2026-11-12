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
        // Set only after an explicit, successful local OEM folder comparison.
        // Never use this user-visible text to authorize installation.
        public string LocalReviewStatus { get; init; } = string.Empty;
        public string Detail =>
            $"{Category} · {Type}. Solo elenco HTML salvato: " +
            "firma, versione, Windows, origine e applicabilità NON verificate." +
            (LocalReviewStatus.Length > 0
                ? " Confronto locale: " + LocalReviewStatus
                : string.Empty);
    }

    public sealed record Report(
        IReadOnlyList<Package> Packages,
        int TotalDistinct,
        int Omitted)
    {
        // Full bounded set of safe package NAMES, held in memory only.
        // UI keeps a 96-row preview. If the user later selects a local
        // folder, an exact filename seen there may be outside this preview.
        // Never retain signed links, credentials, order IDs or raw HTML.
        public IReadOnlyList<Package> AllParsedPackages { get; init; } = [];
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
        // Keep all bounded, validated names before choosing the 96-row
        // preview. Truncating during HTML traversal starved late NVIDIA,
        // Wi-Fi and audio packages when chipset rows appeared first.
        var packages = new List<Package>();
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

            var (category, type) = Categorize(name);
            packages.Add(new Package(name, category, type));
        }

        if (packages.Count == 0)
            throw new InvalidDataException(
                "Nessun nome di pacchetto driver leggibile dalla pagina. " +
                "Il file HTML potrebbe essere incompleto o non essere " +
                "la lista dei download del portale ufficiale.");

        var preview = SelectBalancedPreview(packages);
        return new Report(preview, packages.Count,
            packages.Count - preview.Length)
        {
            AllParsedPackages = packages.ToArray()
        };
    }

    /// <summary>
    /// Show up to 96 safe HTML package names, prioritizing only EXACT
    /// basenames actually observed during a separate, opt-in local folder
    /// scan. Still balance by category so a chipset-heavy saved portal
    /// cannot bury later NVIDIA/Killer/Realtek evidence. Observing a
    /// filename is not authenticity or hardware applicability.
    /// </summary>
    public static IReadOnlyList<Package> ChoosePreviewForLocal(
        IReadOnlyList<Package> allPackages,
        IEnumerable<string> observedFileNames)
    {
        ArgumentNullException.ThrowIfNull(allPackages);
        ArgumentNullException.ThrowIfNull(observedFileNames);
        if (allPackages.Count > MaximumLinks)
            throw new InvalidDataException("Troppi nomi HTML per la riconciliazione.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in allPackages)
        {
            if (item is null || !IsSafePackageName(item.Name) ||
                !names.Add(item.Name))
                throw new InvalidDataException(
                    "Elenco pacchetti HTML non valido o duplicato.");
        }

        // Never incorporate an unchecked local path, or use substring
        // matching between a filename and an HTML package name.
        var observed = observedFileNames
            .Where(name => name is not null && IsSafePackageName(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matched = SelectBalancedPreview(allPackages
            .Where(item => observed.Contains(item.Name)).ToArray());
        if (matched.Length == MaximumDisplayedPackages)
            return matched;

        // Select the remaining category slots BEFORE sorting the preview.
        // Taking 95 rows from an already category-sorted 96-row preview
        // would bury late NVIDIA/Realtek when the 'Chipset' group sorts first.
        var remaining = SelectBalancedPreview(allPackages
            .Where(item => !observed.Contains(item.Name)).ToArray(),
            MaximumDisplayedPackages - matched.Length);
        return matched.Concat(remaining)
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // Display selection ONLY: category is derived from the safe filename,
    // never a device identity or verified update. Sorting before round-robin
    // makes the selected names deterministic even when the HTML row order
    // differs (portal uploads, browser saves, etc.).
    private static Package[] SelectBalancedPreview(
        IReadOnlyList<Package> packages,
        int maximum = MaximumDisplayedPackages)
    {
        var grouped = packages
            .GroupBy(package => package.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new Queue<Package>(group.OrderBy(
                package => package.Name, StringComparer.OrdinalIgnoreCase)))
            .ToArray();

        var selected = new List<Package>(
            Math.Min(packages.Count, maximum));
        while (selected.Count < maximum &&
            grouped.Any(group => group.Count > 0))
        {
            foreach (var group in grouped)
            {
                if (group.Count == 0)
                    continue;
                selected.Add(group.Dequeue());
                if (selected.Count == maximum)
                    break;
            }
        }
        return selected
            .OrderBy(package => package.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    public static bool IsSafePackageName(string name)
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

    // Internal to the read-only local planner: the same fixed HTML
    // filename categories must control fair allocation of scarce EXE
    // overlay probes. Call only with a validated saved package basename.
    internal static (string Category, string Type) Categorize(string name)
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
