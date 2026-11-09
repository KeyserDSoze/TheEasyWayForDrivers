using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Opt-in, no-install Dell Command Update scan using an already installed
/// dcu-cli.exe in a fixed Dell Program Files location. No arbitrary executables
/// or user-controlled CLI parameters are accepted.
/// </summary>
public static class DellApplicableUpdateReader
{
    public sealed record Result(bool NoDriverUpdates, string? Xml);

    public static async Task<Result> QueryCurrentPcAsync(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Dell Command Update richiede Windows.");

        cancellationToken.ThrowIfCancellationRequested();
        var executable = FindInstalledExecutable() ??
            throw new FileNotFoundException(
                "Dell Command Update non trovato nelle cartelle Program Files ufficiali.");

        var temporaryFolder = Directory.CreateTempSubdirectory("OmegaDrive-Dell-").FullName;
        var report = Path.Combine(temporaryFolder, "DellDriverUpdates.xml");

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
            // DCU's /scan command evaluates CURRENT PC. The driver filter is
            // one-time, never a persistent /configure operation.
            start.ArgumentList.Add("/scan");
            start.ArgumentList.Add("-updateType=driver");
            start.ArgumentList.Add("-report=" + report);
            start.ArgumentList.Add("-silent");

            using var process = Process.Start(start) ??
                throw new InvalidOperationException("Dell Command Update non avviato.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));

            try
            {
                // Dell CLI may emit unbounded output on malformed runs.
                // Drain both redirected streams concurrently within strict
                // limits; the shared reader kills the process tree on overflow.
                // Never log stdout/stderr: these may identify the user's PC.
                var stdout = OemCompanionOutputReader.ReadBoundedAsync(
                    process, process.StandardOutput, 32 * 1024, timeout.Token);
                var stderr = OemCompanionOutputReader.ReadBoundedAsync(
                    process, process.StandardError, 32 * 1024, timeout.Token);
                await Task.WhenAll(
                    process.WaitForExitAsync(timeout.Token), stdout, stderr);

                // Dell documents exit 500 as 'no updates found for filters'.
                if (process.ExitCode == 500)
                    return new Result(true, null);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"Dell Command Update: scansione non completata (codice {process.ExitCode}). " +
                        "Controllare privilegi, supporto del modello e connettività.");

                if (!File.Exists(report))
                    throw new InvalidDataException(
                        "Dell Command Update non ha creato il report atteso.");

                var info = new FileInfo(report);
                if (info.Length is <= 0 or > 1024 * 1024)
                    throw new InvalidDataException("Report Dell vuoto o troppo grande.");
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Report Dell non può essere un collegamento.");

                return new Result(false, await File.ReadAllTextAsync(report, timeout.Token));
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
                Directory.Delete(temporaryFolder, recursive: true);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                // Do not turn an otherwise valid scan into a false failure:
                // the report contains vendor metadata, not serial numbers.
            }
        }
    }

    private static string? FindInstalledExecutable()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var relative in new[]
            {
                Path.Combine("Dell", "CommandUpdate", "dcu-cli.exe"),
                Path.Combine("Dell", "Dell Command Update", "dcu-cli.exe")
            })
            {
                var path = Path.Combine(root, relative);
                if (File.Exists(path) &&
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                    return path;
            }
        }

        return null;
    }
}
