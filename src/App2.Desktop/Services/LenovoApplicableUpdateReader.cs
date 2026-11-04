using System.Diagnostics;
using System.IO;
using System.Text;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Explicit, opt-in query to an ALREADY INSTALLED Lenovo.Client.Update module.
/// Get-LnvUpdate evaluates current-machine applicability. We never call Save-,
/// Install-, Expand-LnvUpdate, run a package or bypass signature checks.
/// The module may query/download temporary verification metadata over HTTPS.
/// </summary>
public static class LenovoApplicableUpdateReader
{
    // Immutable script: no user-controlled PowerShell input or external paths.
    // Lenovo.Client.Update v1.0.3+ defers installer downloads to Save/Install.
    private const string Query = """
        $ErrorActionPreference = 'Stop'
        if (-not (Get-Module -ListAvailable -Name Lenovo.Client.Update)) {
            throw 'Lenovo.Client.Update non installato. Installa il modulo ufficiale Lenovo separatamente.'
        }
        Import-Module -Name Lenovo.Client.Update -ErrorAction Stop
        $null = Get-Command -Name Get-LnvUpdate -Module Lenovo.Client.Update -ErrorAction Stop
        $updates = @(
            Get-LnvUpdate -ErrorAction Stop |
                Where-Object {
                    ($_.Type -eq 'Driver' -or $_.Type -eq 2 -or $_.Category -eq 'Driver') -and
                    $_.IsApplicable -eq $true -and $_.IsInstalled -eq $false
                } |
                Select-Object -First 80 -Property PackageID, Title, Version,
                    ReleaseDate, Type, Category, IsApplicable, IsInstalled, Severity
        )
        ConvertTo-Json -InputObject $updates -Depth 4 -Compress
        """;

    public static async Task<string> QueryCurrentPcAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Il modulo Lenovo richiede Windows.");

        cancellationToken.ThrowIfCancellationRequested();

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

        using var process = Process.Start(start) ??
            throw new InvalidOperationException("Impossibile avviare Windows PowerShell.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var result = await stdout;
            var error = await stderr;

            if (result.Length > 1024 * 1024 || error.Length > 16 * 1024)
                throw new InvalidDataException("Il report Lenovo supera i limiti previsti.");
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "Get-LnvUpdate non disponibile o non autorizzato. " +
                    "Verifica modulo ufficiale Lenovo.Client.Update, privilegi e connettività.");
            return result.Trim();
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
