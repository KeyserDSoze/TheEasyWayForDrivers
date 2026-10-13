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

    public static Task<CatalogMemberVerification.Report> VerifyAsync(
        string infPath, string catalogPath, CancellationToken cancellationToken) =>
        VerifyMemberAsync(infPath, catalogPath,
            Path.GetDirectoryName(Path.GetFullPath(infPath))!, cancellationToken);

    /// <summary>
    /// Checks a bounded candidate file under the same package root as the CAT.
    /// A successful result refers ONLY to this file, not an entire package.
    /// </summary>
    public static Task<CatalogMemberVerification.Report> VerifyPayloadAsync(
        string memberPath, string catalogPath, string packageRoot,
        CancellationToken cancellationToken) =>
        VerifyMemberAsync(memberPath, catalogPath, packageRoot, cancellationToken);

    private static async Task<CatalogMemberVerification.Report> VerifyMemberAsync(
        string memberPath, string catalogPath, string packageRoot,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("La verifica SignTool richiede Windows.");

        var extension = Path.GetExtension(memberPath);
        if (!DriverPayloadAuditPolicy.IsCandidateExtension(extension))
            throw new InvalidDataException("Estensione file non ammessa per verifica CAT.");
        var member = ValidateFile(memberPath, extension,
            extension.Equals(".inf", StringComparison.OrdinalIgnoreCase)
                ? MaxInfBytes : DriverPayloadAuditPolicy.MaxIndividualBytes);
        var catalog = ValidateFile(catalogPath, ".cat", MaxCatBytes);
        var root = new DirectoryInfo(Path.GetFullPath(packageRoot));
        if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Cartella del pacchetto non attendibile.");
        if (!string.Equals(root.FullName.TrimEnd(Path.DirectorySeparatorChar),
                catalog.DirectoryName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Il CAT deve trovarsi nella cartella radice del pacchetto.");
        var relative = Path.GetRelativePath(root.FullName, member.FullName);
        if (Path.IsPathRooted(relative) ||
            relative is ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            throw new InvalidDataException("File al di fuori della cartella del pacchetto.");
        var ancestor = member.Directory;
        while (ancestor is not null &&
            !string.Equals(ancestor.FullName.TrimEnd(Path.DirectorySeparatorChar),
                root.FullName.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Cartella link/reparse non consentita.");
            ancestor = ancestor.Parent;
        }

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
        options.ArgumentList.Add(member.FullName);

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
