using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Runs an opt-in read-only HP Image Assistant scan against the current PC.
/// HPIA is installed/extracted separately. We verify Windows Authenticode and
/// a known HP certificate publisher before launching any discovered executable.
/// We do not download HPIA, update packages or install drivers.
/// </summary>
public static class HpImageAssistantReader
{
    public sealed record Result(bool NoRecommendations, string? ReportXml);

    public static async Task<Result> QueryCurrentPcAsync(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("HP Image Assistant richiede Windows.");

        cancellationToken.ThrowIfCancellationRequested();

        var executable = FindInstalledExecutable() ??
            throw new FileNotFoundException(
                "HP Image Assistant non trovato nelle cartelle HP note. " +
                "Installare o estrarre lo strumento HP ufficiale separatamente.");

        await VerifyHpAuthenticodeAsync(executable, cancellationToken);

        var folder = Directory.CreateTempSubdirectory("OmegaDrive-HPIA-").FullName;
        var reportBase = Path.Combine(folder, "Recommendations");
        var xmlFile = reportBase + ".xml";

        try
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            };
            start.ArgumentList.Add("/Operation:Analyze");
            start.ArgumentList.Add("/Category:Drivers");
            start.ArgumentList.Add("/Selection:All");
            start.ArgumentList.Add("/Action:List");
            start.ArgumentList.Add("/Silent");
            start.ArgumentList.Add("/ReportFilePath:" + reportBase);

            using var process = Process.Start(start) ??
                throw new InvalidOperationException("HP Image Assistant non avviato.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));

            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var output = await stdout;
                var errors = await stderr;

                if (output.Length > 32 * 1024 || errors.Length > 32 * 1024)
                    throw new InvalidDataException("Log HP Image Assistant troppo grandi.");

                // HP HPIA user guide: 256 means no recommendations;
                // 257 means no recommendations selected. Other nonzero
                // exit codes include unsupported platform or connectivity.
                if (process.ExitCode is 256 or 257)
                    return new Result(true, null);

                if (process.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"HP Image Assistant: scansione non completata (codice {process.ExitCode}). " +
                        "Verificare modello supportato, rete e privilegi.");

                if (!File.Exists(xmlFile))
                    throw new InvalidDataException(
                        "HP Image Assistant non ha scritto il report XML delle raccomandazioni.");

                var info = new FileInfo(xmlFile);
                if (info.Length is <= 0 or > 4 * 1024 * 1024 ||
                    (info.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Report HP non valido o troppo grande.");

                return new Result(false,
                    await File.ReadAllTextAsync(xmlFile, timeout.Token));
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                throw;
            }
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException)
            {
                // No HPIA report content is included in logs or sent remotely
                // by OmegaDrive. Temp cleanup may fail if HPIA still holds a
                // file handle, especially after timeout.
            }
        }
    }

    private static string? FindInstalledExecutable()
    {
        var programFiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        foreach (var root in programFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var subdir in new[]
            {
                Path.Combine("HP", "HP Image Assistant"),
                Path.Combine("HP", "HPIA"),
                Path.Combine("HP", "HPImageAssistant")
            })
            {
                var candidate = Path.Combine(root, subdir, "HPImageAssistant.exe");
                if (IsOrdinaryFile(candidate))
                    return candidate;
            }
        }

        // HP's own documentation gives C:\SWSetup as the usual extraction
        // location. Search only the first level of spNNNNN folders, bounded;
        // never execute a user-provided path or enumerate arbitrary drives.
        var drive = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrWhiteSpace(drive))
            return null;
        var swSetup = Path.Combine(drive, "SWSetup");
        foreach (var candidate in new[]
        {
            Path.Combine(swSetup, "HPIA", "HPImageAssistant.exe"),
            Path.Combine(swSetup, "HPImageAssistant.exe")
        })
        {
            if (IsOrdinaryFile(candidate))
                return candidate;
        }

        if (!Directory.Exists(swSetup) || IsReparsePoint(swSetup))
            return null;

        foreach (var dir in Directory.EnumerateDirectories(swSetup, "sp*")
            .Where(path => Regex.IsMatch(Path.GetFileName(path),
                @"\Asp\d{4,9}\z", RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant))
            .Take(80))
        {
            if (IsReparsePoint(dir))
                continue;
            var candidate = Path.Combine(dir, "HPImageAssistant.exe");
            if (IsOrdinaryFile(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsOrdinaryFile(string path) =>
        File.Exists(path) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static async Task VerifyHpAuthenticodeAsync(
        string executable, CancellationToken cancellationToken)
    {
        var powershell = Path.Combine(
            Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
            throw new FileNotFoundException(
                "PowerShell richiesto per verificare la firma HPIA.", powershell);

        // Encode even the located path as base64 data, not PowerShell source.
        var pathData = Convert.ToBase64String(Encoding.UTF8.GetBytes(executable));
        var script = """
            $ErrorActionPreference = 'Stop'
            $path = [Text.Encoding]::UTF8.GetString(
                [Convert]::FromBase64String('BASEPATH'))
            $signature = Get-AuthenticodeSignature -LiteralPath $path
            if ($signature.Status -ne 'Valid' -or
                $null -eq $signature.SignerCertificate) { exit 9 }
            $publisher = $signature.SignerCertificate.GetNameInfo(
                [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
                $false)
            if ($publisher -notin @(
                'HP Inc.', 'HP Inc', 'Hewlett-Packard Company',
                'Hewlett-Packard', 'HP Development Company, L.P.')) {
                exit 10
            }
            exit 0
            """.Replace("BASEPATH", pathData, StringComparison.Ordinal);

        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));

        using var verifier = Process.Start(start) ??
            throw new InvalidOperationException("Controllo firma HPIA non avviato.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await verifier.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!verifier.HasExited)
                verifier.Kill(entireProcessTree: true);
            throw;
        }

        if (verifier.ExitCode != 0)
            throw new InvalidDataException(
                "HPIA non ha una firma Authenticode valida di HP; " +
                "esecuzione bloccata. Scaricare lo strumento dal sito ufficiale HP.");
    }
}
