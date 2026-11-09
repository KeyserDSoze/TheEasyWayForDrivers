using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Read-only NVIDIA GeForce editorial RSS driver ANNOUNCEMENTS, not a
/// hardware-matched driver catalog or a source of installable packages.
/// Optional on-demand usage only; NVIDIA's RSS terms are non-commercial.
/// </summary>
public static class NvidiaGeForceDriverNews
{
    public const string FeedUrl =
        "https://www.nvidia.com/content/rss/geforce/news/index.php";
    public const int MaximumFeedBytes = 512 * 1024;
    public const int MaximumAnnouncements = 4;
    public const int MaximumAgeDays = 90;

    public sealed record Announcement(
        string Title,
        DateTimeOffset Published,
        Uri OfficialArticle)
    {
        public bool IsInstallable => false;
        public bool IsVerifiedForDevice => false;
        public bool IsDriverCatalogResult => false;
    }

    public static IReadOnlyList<Announcement> Parse(
        string xml, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (xml.Length > MaximumFeedBytes)
            throw new InvalidDataException("Feed NVIDIA oltre il limite.");

        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumFeedBytes,
            MaxCharactersFromEntities = 0,
            IgnoreWhitespace = true
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        if (document.Root?.Name.LocalName != "rss" ||
            document.Root.Element("channel") is not { } channel)
            throw new InvalidDataException("Il feed NVIDIA non è RSS riconosciuto.");

        // The 512 KiB XML input cap bounds the entire feed. Apply the
        // four-item presentation limit AFTER checking all entries: an
        // NVIDIA release can follow dozens of unrelated news articles.
        var byArticle = new Dictionary<string, Announcement>(
            StringComparer.OrdinalIgnoreCase);
        var cutoff = now.AddDays(-MaximumAgeDays);
        foreach (var item in channel.Elements("item"))
        {
            var title = item.Element("title")?.Value?.Trim();
            var link = item.Element("link")?.Value?.Trim();
            var rawDate = item.Element("pubDate")?.Value?.Trim();
            if (string.IsNullOrEmpty(title) || title.Length > 200 ||
                string.IsNullOrEmpty(link) ||
                string.IsNullOrWhiteSpace(rawDate) ||
                !LooksLikeGeForceDriverAnnouncement(title))
                continue;

            if (!DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var published) ||
                published < cutoff || published > now.AddDays(1) ||
                !Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
                !IsOfficialGeForceArticle(uri))
                continue;

            // Editorial text is untrusted: do not show HTML tags, control
            // characters, bidirectional override marks, or very long labels.
            var safeTitle = new string(title
                // The current official GeForce news feed is English. Only
                // printable ASCII can enter this short UI label: this
                // removes every Unicode bidi override/format mark, control,
                // surrogate and HTML-like angle bracket by construction.
                .Select(ch => ch >= ' ' && ch <= '~' &&
                    ch != '<' && ch != '>' ? ch : ' ')
                .Take(140).ToArray()).Trim();
            // Remove tracking parameters before deduplication so two
            // links to one article cannot inflate published announcements.
            var cleanArticle = new Uri(uri.GetLeftPart(UriPartial.Path));
            if (safeTitle.Length < 10)
                continue;

            // Two versions of the same editorial link must count once,
            // using its newest valid date regardless of feed ordering.
            var key = cleanArticle.AbsoluteUri;
            if (!byArticle.TryGetValue(key, out var existing) ||
                published > existing.Published)
                byArticle[key] = new Announcement(
                    safeTitle, published, cleanArticle);
        }
        return byArticle.Values
            .OrderByDescending(item => item.Published)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumAnnouncements)
            .ToArray();
    }

    private static bool LooksLikeGeForceDriverAnnouncement(string title)
    {
        // Never equate RTX Spark-only firmware/driver releases with GeForce
        // notebook GPU support, or generic articles mentioning "drivers".
        if (title.Contains("RTX Spark", StringComparison.OrdinalIgnoreCase) &&
            !title.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
            return false;
        return title.Contains("Game Ready Driver", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Studio Driver", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOfficialGeForceArticle(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IdnHost.Equals("www.nvidia.com", StringComparison.OrdinalIgnoreCase) &&
        uri.Port == 443 &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        uri.AbsolutePath.StartsWith("/en-us/geforce/news/",
            StringComparison.OrdinalIgnoreCase) &&
        !uri.AbsolutePath.Contains("..", StringComparison.Ordinal);
}
