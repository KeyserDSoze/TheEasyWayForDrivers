using System.Diagnostics;
using System.IO;
using System.Text;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Explicit read-only lookup through locally installed HP CMSL Get-SoftpaqList.
/// The cmdlet may fetch HP metadata; it does not download driver packages.
/// </summary>
public static class HpSoftpaqCatalogReader
{
    // OS and platform resolution by HP CMSL on the LOCAL PC.
    // No -Download/-DownloadMetadata flags and no arbitrary script input.
    private const string Query = """
        $ErrorActionPreference = 'Stop'
        if (-not (Get-Command Get-SoftpaqList -ErrorAction SilentlyContinue)) {
            throw 'HP CMSL non installata: Get-SoftpaqList non disponibile.'
        }
        @(Get-SoftpaqList -Os 'win11' -Category 'Driver' -Quiet |
            Select-Object -First 50) | ConvertTo-Json -Depth 4 -Compress
        """;

    public static async Task<string> QueryCurrentPcAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Catalogo HP supportato solo su Windows.");

        var executable = Path.Combine(
            Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("Windows PowerShell non disponibile.", executable);

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(Query)));

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Impossibile avviare PowerShell.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var result = await stdoutTask;
            var error = await stderrTask;
            if (result.Length > 1024 * 1024 || error.Length > 16 * 1024)
                throw new InvalidDataException("La risposta HP supera i limiti previsti.");
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "HP CMSL non ha restituito i driver: controlla che il modulo sia installato e il modello supportato.");
            return result.Trim();
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
