using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Explicit opt-in fetch of one fixed official NVIDIA GeForce RSS URL.
/// No device model, ID, serial, driver version or private data is transmitted.
/// No redirects, scripts, downloads, installers, WUA changes or firmware.
/// </summary>
public static class NvidiaDriverNewsReader
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip |
            DecompressionMethods.Deflate
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public static async Task<IReadOnlyList<NvidiaGeForceDriverNews.Announcement>>
        ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(12));

        // No query string or user-derived headers/IDs are ever added.
        using var request = new HttpRequestMessage(HttpMethod.Get,
            NvidiaGeForceDriverNews.FeedUrl);
        request.Headers.TryAddWithoutValidation("Accept",
            "application/rss+xml, application/xml, text/xml");

        using var response = await Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead,
            linked.Token);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidDataException(
                "Feed NVIDIA non verificabile; nessuna conclusione sui driver.");

        if (response.Content.Headers.ContentLength is > NvidiaGeForceDriverNews.MaximumFeedBytes)
            throw new InvalidDataException(
                "Feed NVIDIA oltre il limite massimo di 512 KiB.");

        await using var stream = await response.Content.ReadAsStreamAsync(
            linked.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, linked.Token);
            if (read == 0)
                break;
            if (buffer.Length + read > NvidiaGeForceDriverNews.MaximumFeedBytes)
                throw new InvalidDataException(
                    "Feed NVIDIA oltre il limite massimo di 512 KiB.");
            buffer.Write(chunk, 0, read);
        }

        // RSS is XML; use UTF-8 strictly, not a locale-specific Windows ANSI
        // conversion or a browser/HTML parser.
        var utf8 = new UTF8Encoding(false, true);
        var xml = utf8.GetString(buffer.ToArray()).TrimStart('\uFEFF');
        return NvidiaGeForceDriverNews.Parse(xml, DateTimeOffset.UtcNow);
    }
}
