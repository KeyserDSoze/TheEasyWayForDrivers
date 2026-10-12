using System.Diagnostics;
using System.Net;
using TheEasyWayForDrivers.Core.Drivers;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>Downloads Dell's published CAB and extracts ONLY catalog XML in a
/// temporary directory as an unprivileged desktop operation. No packages run.</summary>
public sealed class DellCatalogReader
{
    private const long MaxCabBytes = 24 * 1024 * 1024;
    private const long MaxXmlBytes = 16 * 1024 * 1024;

    public async Task<IReadOnlyList<DellDriverPackCatalog.Pack>> FindAsync(
        string model,
        CancellationToken token)
    {
        var temp = Path.Combine(Path.GetTempPath(), "OmegaDrive-DellCatalog-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var cab = Path.Combine(temp, "DriverPackCatalog.cab");
            var url = new Uri(DellDriverPackCatalog.OfficialCatalogUrl);
            // Only an allowlisted Dell CDN host; no automatic redirect to
            // third-party content and no arbitrary executable URLs.
            using var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
            using var response = await client.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
                throw new InvalidDataException("Dell catalog redirected: refusing an unverified host.");
            if (response.RequestMessage?.RequestUri is not { } finalUri ||
                finalUri.Scheme != Uri.UriSchemeHttps ||
                (finalUri.Host != "downloads.dell.com" &&
                 finalUri.Host != "dl.dell.com"))
                throw new InvalidDataException("Catalog source is not an official Dell HTTPS host.");

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxCabBytes)
                throw new InvalidDataException("Dell catalog is larger than expected.");

            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var target = File.Create(cab))
            {
                var bytes = new byte[64 * 1024];
                long received = 0;
                while (true)
                {
                    var read = await source.ReadAsync(bytes, token);
                    if (read == 0) break;
                    received += read;
                    if (received > MaxCabBytes)
                        throw new InvalidDataException("Dell catalog size limit exceeded.");
                    await target.WriteAsync(bytes.AsMemory(0, read), token);
                }
            }

            // Windows built-in CAB extraction; no code from the catalog runs.
            // Extract one known file name, not the full untrusted archive.
            var args = new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "expand.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            args.ArgumentList.Add("-F:DriverPackCatalog.xml");
            args.ArgumentList.Add(cab);
            args.ArgumentList.Add(temp);

            using (var process = Process.Start(args)
                ?? throw new InvalidOperationException("Windows expand.exe was not available."))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    throw;
                }

                if (process.ExitCode != 0)
                    throw new InvalidDataException("Unable to read the Dell catalog CAB.");
            }

            var xmlPath = Path.Combine(temp, "DriverPackCatalog.xml");
            var file = new FileInfo(xmlPath);
            if (!file.Exists || file.Length > MaxXmlBytes)
                throw new InvalidDataException("Dell catalog XML missing or too large.");

            await using var xml = File.OpenRead(xmlPath);
            return DellDriverPackCatalog.FindForModel(xml, model);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
