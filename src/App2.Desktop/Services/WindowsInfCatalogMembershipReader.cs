using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Opt-in read-only validation that one INF is an authenticated member of
/// the explicitly selected CAT under Windows driver-signing policy.
/// Uses the Microsoft Windows SDK SignTool, if already installed.
/// </summary>
public static class WindowsInfCatalogMembershipReader
{
    private const long MaxInfBytes = 1024 * 1024;
    private const long MaxCatBytes = 32 * 1024 * 1024;

    public static async Task<CatalogMemberVerification.Report> VerifyAsync(
        string infPath, string catalogPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("La verifica SignTool richiede Windows.");

        var inf = ValidateFile(infPath, ".inf", MaxInfBytes);
        var catalog = ValidateFile(catalogPath, ".cat", MaxCatBytes);
        if (!string.Equals(inf.DirectoryName, catalog.DirectoryName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "INF e catalogo devono trovarsi nella stessa cartella locale.");

        var sdkSignTool = FindInstalledSdkSignTool();
        if (sdkSignTool is null)
            return CatalogMemberVerification.MissingSdk();

        var options = new ProcessStartInfo(sdkSignTool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.SystemDirectory
        };

        // Documented Microsoft command:
        // SignTool verify /kp /c CatalogFileName.cat DriverFileName.inf
        // Separate arguments prevent shell/command injection by file names.
        options.ArgumentList.Add("verify");
        options.ArgumentList.Add("/kp");
        options.ArgumentList.Add("/c");
        options.ArgumentList.Add(catalog.FullName);
        options.ArgumentList.Add(inf.FullName);

        using var process = Process.Start(options)
            ?? throw new InvalidOperationException("Impossibile avviare SignTool del Windows SDK.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        try
        {
            // Drain both pipes in parallel to avoid process deadlocks.
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var errors = await errorTask;
            if (output.Length > 32 * 1024 || errors.Length > 32 * 1024)
                throw new InvalidDataException("SignTool ha restituito troppi dati.");
            // Do not display arbitrary SignTool output containing user file paths.
            // Only exit code 0 counts as a verified member.
            return CatalogMemberVerification.FromSignToolExitCode(process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static FileInfo ValidateFile(string path, string extension, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("Non utilizzare percorsi UNC o di dispositivo per la verifica.");
        var file = new FileInfo(fullPath);
        if (!file.Exists || !string.Equals(file.Extension, extension,
                StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("File INF/CAT locale inesistente o con estensione non valida.");
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Link simbolici e reparse point non consentiti per INF/CAT.");
        if (file.Length > maxBytes)
            throw new InvalidDataException("File INF/CAT oltre il limite previsto.");
        return file;
    }

    private static string? FindInstalledSdkSignTool()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrWhiteSpace(programFiles))
            return null;
        var root = Path.Combine(programFiles, "Windows Kits", "10", "bin");
        if (!Directory.Exists(root))
            return null;

        var versions = Directory.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .OfType<string>();
        var candidates = WindowsSdkSignToolLocator.CandidatePaths(
            programFiles, RuntimeInformation.OSArchitecture.ToString(), versions);
        return candidates.FirstOrDefault(path =>
            File.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0);
    }
}
