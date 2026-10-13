using System.Diagnostics;
using System.IO;
using System.Text;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// On-demand inspection of a local CAT's Authenticode signature by the
/// Windows built-in PowerShell command. Does not verify INF/SYS membership.
/// </summary>
public static class WindowsCatalogSignatureReader
{
    private const long MaxCatalogBytes = 32 * 1024 * 1024;
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $sig = Get-AuthenticodeSignature -LiteralPath $env:OMEGADRIVE_CAT_PATH -ErrorAction Stop
        $signer = if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { '' }
        [pscustomobject]@{
            Status = $sig.Status.ToString()
            Signer = $signer
        } | ConvertTo-Json -Compress -Depth 2
        """;

    public static async Task<CatalogTrustReport.Result> InspectAsync(
        string catalogPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Verifica firme CAT disponibile solo su Windows.");
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        var file = new FileInfo(catalogPath);
        if (!file.Exists || !file.Extension.Equals(".cat", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("File di catalogo CAT locale mancante.", catalogPath);
        if (file.Length > MaxCatalogBytes)
            throw new InvalidDataException("Catalogo CAT oltre il limite di 32 MiB.");

        var executable = Path.Combine(Environment.SystemDirectory,
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("Windows PowerShell non disponibile.", executable);

        var options = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        options.ArgumentList.Add("-NoProfile");
        options.ArgumentList.Add("-NonInteractive");
        options.ArgumentList.Add("-EncodedCommand");
        options.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)));
        // The path is never interpolated into executable PowerShell source.
        options.Environment["OMEGADRIVE_CAT_PATH"] = file.FullName;

        using var process = Process.Start(options)
            ?? throw new InvalidOperationException("Impossibile avviare Windows PowerShell.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
            var stderr = process.StandardError.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token);
            var result = await stdout;
            var error = await stderr;
            if (result.Length > 16 * 1024 || error.Length > 16 * 1024)
                throw new InvalidDataException("Risposta Windows signature oltre il limite previsto.");
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "Windows non ha potuto verificare la firma CAT; nessuna verifica di integrità confermata.");

            return CatalogTrustReport.Parse(result);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
