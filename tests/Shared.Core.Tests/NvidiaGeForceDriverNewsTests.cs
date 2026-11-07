using System.Xml;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class NvidiaGeForceDriverNewsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static string Item(
        string title,
        string url = "https://www.nvidia.com/en-us/geforce/news/driver-release/",
        string date = "Wed, 04 Nov 2026 10:30:00 GMT") =>
        $"""
        <item>
          <title>{System.Security.SecurityElement.Escape(title)}</title>
          <link>{System.Security.SecurityElement.Escape(url)}</link>
          <pubDate>{date}</pubDate>
        </item>
        """;

    private static string Rss(params string[] items) =>
        "<rss version=\"2.0\"><channel><title>NVIDIA GeForce News</title>" +
        string.Join("", items) + "</channel></rss>";

    [Fact]
    public void OfficialRecentGameReadyAndStudioItemsAppearAsNewsNotDeviceUpdates()
    {
        var rss = Rss(
            Item("GeForce Game Ready Driver Released For Latest Games",
                "https://www.nvidia.com/en-us/geforce/news/game-ready-a/"),
            Item("NVIDIA Studio Driver Released For Creators",
                "https://www.nvidia.com/en-us/geforce/news/studio-b/",
                "Tue, 03 Nov 2026 08:00:00 GMT"),
            Item("New NVIDIA RTX Graphics Card Announced",
                "https://www.nvidia.com/en-us/geforce/news/graphics-card/"),
            Item("DLSS Games Update",
                "https://www.nvidia.com/en-us/geforce/news/dlss-games/"));

        var result = NvidiaGeForceDriverNews.Parse(rss, Now);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Title.Contains("Game Ready Driver"));
        Assert.Contains(result, item => item.Title.Contains("Studio Driver"));
        Assert.All(result, item =>
        {
            Assert.False(item.IsInstallable);
            Assert.False(item.IsDriverCatalogResult);
            Assert.False(item.IsVerifiedForDevice);
            Assert.Equal("www.nvidia.com", item.OfficialArticle.Host);
        });
    }

    [Fact]
    public void NvidiaGeForceNewsletterDoesNotPretendToCheckCurrentGpuVersion()
    {
        var result = NvidiaGeForceDriverNews.Parse(
            Rss(Item("GeForce Game Ready Driver Released")), Now);
        Assert.Single(result);
        Assert.False(result[0].IsInstallable);
        Assert.False(result[0].IsVerifiedForDevice);
    }

    [Theory]
    [InlineData("http://www.nvidia.com/en-us/geforce/news/driver/")]
    [InlineData("https://evil.example/en-us/geforce/news/driver/")]
    [InlineData("https://www.nvidia.com.evil.example/en-us/geforce/news/driver/")]
    [InlineData("https://www.nvidia.com:8443/en-us/geforce/news/driver/")]
    [InlineData("https://me@www.nvidia.com/en-us/geforce/news/driver/")]
    [InlineData("https://www.nvidia.com/en-us/download/driver.exe")]
    [InlineData("https://www.nvidia.com/en-us/geforce/gaming/")]
    [InlineData("file:///C:/Windows/System32/driver.exe")]
    public void UntrustedArticleUrlsAreNotPresented(string url)
    {
        Assert.Empty(NvidiaGeForceDriverNews.Parse(
            Rss(Item("GeForce Game Ready Driver Released", url)), Now));
    }

    [Fact]
    public void QueryParametersAndTrackingFragmentsAreNeverDisplayed()
    {
        var result = NvidiaGeForceDriverNews.Parse(
            Rss(Item("GeForce Studio Driver Released",
                "https://www.nvidia.com/en-us/geforce/news/studio-b/?utm_id=SECRET#private")), Now);
        var row = Assert.Single(result);
        Assert.Equal("https://www.nvidia.com/en-us/geforce/news/studio-b/",
            row.OfficialArticle.AbsoluteUri);
        Assert.DoesNotContain("SECRET", row.OfficialArticle.AbsoluteUri);
        Assert.DoesNotContain("private", row.OfficialArticle.AbsoluteUri);
    }

    [Fact]
    public void DuplicateLinksDoNotCountAsAdditionalReleases()
    {
        var rss = Rss(
            Item("GeForce Game Ready Driver Released",
                "https://www.nvidia.com/en-us/geforce/news/game-a/"),
            Item("Same GeForce Game Ready Driver Released",
                "https://www.nvidia.com/en-us/geforce/news/game-a/"),
            Item("NVIDIA Studio Driver Released",
                "https://www.nvidia.com/en-us/geforce/news/studio-a/"));
        var result = NvidiaGeForceDriverNews.Parse(rss, Now);
        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData("Tue, 01 Jan 2024 00:00:00 GMT")]
    [InlineData("Tue, 10 Nov 2026 00:00:00 GMT")]
    [InlineData("not-a-date")]
    public void OldFutureOrUnreadableDatesMustNotClaimRecentDriverNews(
        string date)
    {
        Assert.Empty(NvidiaGeForceDriverNews.Parse(
            Rss(Item("GeForce Game Ready Driver Released",
                date: date)), Now));
    }

    [Theory]
    [InlineData("NVIDIA DLSS Games and Driver Settings")]
    [InlineData("NVIDIA RTX Spark Game Ready Driver Released")]
    [InlineData("New GeForce GPUs Announced")]
    [InlineData("GeForce Experience Driver Update Tips")]
    [InlineData("NVIDIA RTX Spark Studio Driver Released")]
    public void UnrelatedOrRtxSparkOnlyNewsCannotBecomeGeForceDriverReleases(
        string title)
    {
        Assert.Empty(NvidiaGeForceDriverNews.Parse(
            Rss(Item(title)), Now));
    }

    [Fact]
    public void NewsTitleMarkupAndUnicodeOverridesAreStripped()
    {
        var rss = Rss(Item(
            "GeForce Game Ready Driver Released \u202E&lt;script&gt;X&lt;/script&gt;"));
        var row = Assert.Single(NvidiaGeForceDriverNews.Parse(rss, Now));
        Assert.DoesNotContain("\u202E", row.Title);
        Assert.DoesNotContain("<", row.Title);
        Assert.DoesNotContain(">", row.Title);
        Assert.DoesNotContain("script>", row.Title);
    }

    [Fact]
    public void FeedIsBoundedToFourMostRecentWithinFirstEightyItems()
    {
        var feed = Rss(Enumerable.Range(0, 100).Select(i =>
            Item("GeForce Game Ready Driver Released " + i,
                $"https://www.nvidia.com/en-us/geforce/news/game-{i}/")).ToArray());
        var result = NvidiaGeForceDriverNews.Parse(feed, Now);
        Assert.Equal(NvidiaGeForceDriverNews.MaximumAnnouncements, result.Count);
        Assert.DoesNotContain(result, item =>
            item.OfficialArticle.AbsoluteUri.Contains("game-80/"));
        Assert.All(result, item => Assert.False(item.IsInstallable));
    }

    [Fact]
    public void DtdAndExternalEntityPayloadsAreRejectedWithoutNetworkFileAccess()
    {
        var payload = """
        <!DOCTYPE rss [
          <!ENTITY xxe SYSTEM "file:///C:/Users/SECRET/identity.txt">
        ]>
        <rss version="2.0"><channel><title>&xxe;</title></channel></rss>
        """;
        Assert.Throws<XmlException>(() =>
            NvidiaGeForceDriverNews.Parse(payload, Now));
    }

    [Fact]
    public void OversizeMalformedAndHtmlResponsesAreNotAcceptedAsFeed()
    {
        Assert.Throws<InvalidDataException>(() =>
            NvidiaGeForceDriverNews.Parse(
                new string('x', NvidiaGeForceDriverNews.MaximumFeedBytes + 1),
                Now));
        Assert.Throws<InvalidDataException>(() =>
            NvidiaGeForceDriverNews.Parse(
                "<html><title>Fake GeForce Game Ready Driver</title></html>",
                Now));
        Assert.Throws<XmlException>(() =>
            NvidiaGeForceDriverNews.Parse("<rss><channel>", Now));
    }

    [Fact]
    public void OfficialFeedEndpointIsFixedAndDoesNotContainComputerIdentifiers()
    {
        Assert.Equal(
            "https://www.nvidia.com/content/rss/geforce/news/index.php",
            NvidiaGeForceDriverNews.FeedUrl);
        Assert.DoesNotContain("?", NvidiaGeForceDriverNews.FeedUrl);
        Assert.DoesNotContain("SUBSYS", NvidiaGeForceDriverNews.FeedUrl);
        Assert.DoesNotContain("PRIVATE", NvidiaGeForceDriverNews.FeedUrl);
        Assert.False(NvidiaGeForceDriverNews.Parse(Rss(), Now).Any());
    }
}
