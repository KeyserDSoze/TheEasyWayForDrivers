using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Regression #394: VendorSourceButton_Click uses the URL policy as its
/// final browser-open gate. An approved host is not enough: a path/query
/// can carry private identifiers or redirect somewhere unintended.
/// </summary>
public sealed class VendorOfficialLinkPolicyTests
{
    [Theory]
    [InlineData("https://www.intel.com/content/www/us/en/support/detect.html")]
    [InlineData("https://www.intel.com/content/www/us/en/download/19351/intel-wireless-wi-fi-drivers-for-windows-10-and-windows-11.html")]
    [InlineData("https://www.intel.com/content/www/us/en/download/18649/intel-wireless-bluetooth-drivers-for-windows-10-and-windows-11.html")]
    [InlineData("https://www.nvidia.com/Download/Find.aspx")]
    [InlineData("https://www.amd.com/en/support/download/drivers.html")]
    [InlineData("https://www.realtek.com/Download")]
    [InlineData("https://www.broadcom.com/support/download-search")]
    [InlineData("https://www.qualcomm.com/drivers")]
    [InlineData("https://www.mediatek.com/company/contact-us")]
    [InlineData("https://support.logi.com/")]
    [InlineData("https://mysupport.razer.com/")]
    [InlineData("https://www.dell.com/support/home")]
    [InlineData("https://www.dell.com/support/home/")]
    [InlineData("https://support.hp.com/")]
    [InlineData("https://support.lenovo.com/")]
    [InlineData("https://www.nvidia.com/en-us/geforce/news/game-ready-driver-release/")]
    public void KnownOemDestinationsAndCanonicalNvidiaNewsRemainOpenable(string url)
    {
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(url));
    }

    [Theory]
    [InlineData("https://www.intel.com/content/www/us/en/download/19351/hello")]
    [InlineData("https://www.intel.com/content/www/us/en/support/detect.html?serial=PRIVATE")]
    [InlineData("https://www.nvidia.com/Download/Find.aspx?order=PRIVATE")]
    [InlineData("https://www.nvidia.com/Download/Find.aspx#PRIVATE")]
    [InlineData("https://www.nvidia.com/en-us/geforce/news/new-release/?utm_id=PRIVATE")]
    [InlineData("https://www.nvidia.com/en-us/geforce/news/new-release/#PRIVATE")]
    [InlineData("https://www.nvidia.com/en-us/geforce/news/%2e%2e/redirect")]
    [InlineData("https://www.nvidia.com/en-us/geforce/news/a%2fb/")]
    [InlineData("https://www.nvidia.com/en-us/geforce/news//redirect/")]
    [InlineData("https://www.nvidia.com/en-us/geforce/news/")]
    [InlineData("https://www.nvidia.com/en-us/geforce/driver/")]
    [InlineData("https://www.nvidia.com/redirect?to=https://untrusted.example")]
    [InlineData("https://www.dell.com/support/home/?serial=PRIVATE")]
    [InlineData("https://support.hp.com/private/driver?serial=PRIVATE")]
    [InlineData("https://support.lenovo.com/redirect")]
    [InlineData("https://support.logi.com/?device=PRIVATE")]
    [InlineData("https://mysupport.razer.com/extra")]
    [InlineData("https://www.intel.com.evil.invalid/content/www/us/en/support/detect.html")]
    [InlineData("https://user@www.nvidia.com/en-us/geforce/news/driver/")]
    [InlineData("https://www.nvidia.com:8443/en-us/geforce/news/driver/")]
    [InlineData("http://www.nvidia.com/en-us/geforce/news/driver/")]
    [InlineData("file:///C:/Windows/Temp/driver.html")]
    public void UnapprovedPathsAndPersonalizedOrMalformedLinksAreNotOpenable(string url)
    {
        Assert.False(VendorSourceDiscovery.IsTrustedOfficialUrl(url));
    }

    [Fact]
    public void NoFixedSupportDestinationCanGainTrackingParameters()
    {
        var destinations = new[]
        {
            VendorSourceDiscovery.IntelDsaUrl,
            VendorSourceDiscovery.IntelWifiUrl,
            VendorSourceDiscovery.IntelBluetoothUrl,
            VendorSourceDiscovery.NvidiaDriversUrl,
            VendorSourceDiscovery.AmdDriversUrl,
            VendorSourceDiscovery.RealtekSupportUrl,
            VendorSourceDiscovery.BroadcomSupportUrl,
            VendorSourceDiscovery.QualcommSupportUrl,
            VendorSourceDiscovery.MediaTekSupportUrl,
            VendorSourceDiscovery.LogitechSupportUrl,
            VendorSourceDiscovery.RazerSupportUrl,
            DellApplicableUpdateParser.OfficialSupportUrl,
            HpImageAssistantReportParser.OfficialSupportUrl,
            LenovoApplicableUpdateParser.OfficialSupportUrl
        };
        Assert.All(destinations, url =>
        {
            Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(url));
            Assert.False(VendorSourceDiscovery.IsTrustedOfficialUrl(url + "?id=PRIVATE"));
            Assert.False(VendorSourceDiscovery.IsTrustedOfficialUrl(url + "#PRIVATE"));
        });
        Assert.False(VendorSourceDiscovery.IsTrustedOfficialUrl(null));
        Assert.False(VendorSourceDiscovery.IsTrustedOfficialUrl(""));
    }

    [Fact]
    public void OnlySanitizedRssArticleIsOpenableAndNeverInstallable()
    {
        var now = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
        const string rss = """
            <rss><channel><item>
              <title>GeForce Game Ready Driver Released</title>
              <link>https://www.nvidia.com/en-us/geforce/news/release/?token=PRIVATE#PRIVATE</link>
              <pubDate>Thu, 12 Nov 2026 10:00:00 GMT</pubDate>
            </item></channel></rss>
            """;
        // RSS XML must escape '&' in a query; this synthetic URL has
        // one token and a fragment so it is already valid XML.
        var article = Assert.Single(NvidiaGeForceDriverNews.Parse(rss, now));
        Assert.Equal("https://www.nvidia.com/en-us/geforce/news/release/",
            article.OfficialArticle.AbsoluteUri);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(
            article.OfficialArticle.AbsoluteUri));
        Assert.False(VendorSourceDiscovery.IsTrustedOfficialUrl(
            "https://www.nvidia.com/en-us/geforce/news/release/?token=PRIVATE#PRIVATE"));
        Assert.False(article.IsInstallable);
        Assert.False(article.IsVerifiedForDevice);
    }
}
