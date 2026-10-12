using System.Diagnostics;
using System.IO;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Opt-in read-only query to the already installed official Lenovo.Client.Scripting
/// PowerShell module. Returns vendor metadata, never runs installers.
/// </summary>
public static class LenovoCatalogReader
{
    // Constant script: user input is passed only via an environment variable.
    // PackageType 2 = drivers, excluding firmware (4) and BIOS (3).
    private const string Query = """
        $ErrorActionPreference = 'Stop'
        if (-not (Get-Module -ListAvailable -Name Lenovo.Client.Scripting)) {
            throw 'Lenovo.Client.Scripting non installato. Consulta la documentazione Lenovo.'
        }
        Import-Module Lenovo.Client.Scripting -ErrorAction Stop
        @(Find-LnvUpdate -MachineType $env:OMEGADRIVE_LENOVO_MACHINE_TYPE -WindowsVersion '11' -PackageType '2' -ListAll |
            Select-Object -First 50 ID,Name,Category,Version,PackageType,ReleaseDate) | ConvertTo-Json -Depth 3 -Compress
        """;

    public static async Task<string> QueryAsync(
        string machineType, CancellationToken cancellationToken)
    {
        if (!LenovoMachineTypePolicy.TryNormalize(machineType, out var normalized))
            throw new ArgumentException(
                "Il MachineType Lenovo deve contenere esattamente 4 caratteri alfanumerici.",
                nameof(machineType));

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
        start.Environment["OMEGADRIVE_LENOVO_MACHINE_TYPE"] = normalized;

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Impossibile avviare Windows PowerShell.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            // Read both streams concurrently to avoid a full redirected pipe deadlock.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var result = await stdoutTask;
            var error = await stderrTask;

            if (result.Length > 1024 * 1024 || error.Length > 16 * 1024)
                throw new InvalidDataException("La risposta Lenovo supera i limiti di sicurezza.");
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "La ricerca Lenovo non è riuscita: verifica modulo ufficiale e MachineType.");

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
