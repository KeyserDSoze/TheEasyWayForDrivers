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
    // HP CMSL uses the current platform, OS, version and bitness when the
    // corresponding parameters are omitted. Newer versions expose the
    // Get-HPSoftpaqList alias; older installations retain Get-SoftpaqList.
    // Only metadata: no -Download/-DownloadMetadata/-DownloadNotes.
    private const string Query = """
        $ErrorActionPreference = 'Stop'
        $cmd = if (Get-Command Get-HPSoftpaqList -ErrorAction SilentlyContinue) {
            'Get-HPSoftpaqList'
        } elseif (Get-Command Get-SoftpaqList -ErrorAction SilentlyContinue) {
            'Get-SoftpaqList'
        } else {
            throw 'HP CMSL non installata: Get-HPSoftpaqList/Get-SoftpaqList non disponibile.'
        }
        $categories = @('Driver')
        if ($cmd -eq 'Get-HPSoftpaqList') {
            # CMSL 1.8.5+ documents separate categories not always captured
            # by a simple "Driver" filter; no software/firmware/driverpacks.
            $categories += @(
                'Driver - Graphics', 'Driver - Chipset',
                'Driver - Audio', 'Driver - Keyboard, Mouse and Input Devices',
                'Driver - Enabling', 'Driver - Network',
                'Driver - Storage', 'Driver - Controller'
            )
        }
        $result = @(& $cmd -Category $categories -Quiet |
            Select-Object -First 120 -Property Id, Name, Version,
                ReleaseType, ReleaseDate, Category)
        ConvertTo-Json -InputObject $result -Depth 4 -Compress
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
