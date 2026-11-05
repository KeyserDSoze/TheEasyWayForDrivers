using System.Diagnostics;
using System.IO.Compression;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Narrow, read-only Windows CAB -> INF comparison. Uses only Microsoft's
/// System32 expand.exe with -F:*.inf, never a vendor setup/EXE. Validates the
/// CAB directory BEFORE extracting, stages only INF files in a fresh private
/// temporary folder, validates every extracted output against the directory,
/// and reuses the existing bounded ZIP/INF/PNP comparison pipeline.
/// </summary>
public static class LocalOemCabComparison
{
    private const int MaxExtractedInfBytes = OemZipInfEvidence.MaxTotalInfBytes;
    private const int MaxFolderDepth = 16;
    private const int MaxOutputItems = 1024;

    public static async Task<LocalOemDriverComparison.Report> CompareAsync(
        string cabPath, IReadOnlyList<DriverInfo> devices,
        string architecture, int osMajor, int osMinor, int osBuild,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Il confronto CAB richiede Windows.");
        ArgumentException.ThrowIfNullOrWhiteSpace(cabPath);
        ArgumentNullException.ThrowIfNull(devices);
        if (devices.Count > 2048)
            throw new InvalidDataException("Troppi dispositivi per il confronto CAB.");
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(cabPath);
        if (!Path.GetExtension(fullPath).Equals(".cab", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Seleziona un file CAB, non un programma EXE/MSI.");
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length is < 36 or > OemArchiveInspector.MaxArchiveBytes ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Archivio CAB non leggibile o non consentito.");

        // Keep the read handle open during expand.exe to prevent replacing the
        // originally inspected cabinet with different file contents.
        using var cab = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.SequentialScan);
        var inventory = OemCabIndex.Inspect(cab);
        var infFiles = inventory.Entries
            .Where(entry => entry.Path.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (infFiles.Length > OemZipInfEvidence.MaxInfFiles)
            throw new InvalidDataException("Numero di INF CAB superiore al limite.");
        long totalDeclared = 0;
        var byPath = new Dictionary<string, OemCabIndex.Entry>(StringComparer.OrdinalIgnoreCase);
        var byBaseName = new Dictionary<string, OemCabIndex.Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var inf in infFiles)
        {
            if (inf.DeclaredSize > OemZipInfEvidence.MaxInfBytes)
                throw new InvalidDataException("Un INF CAB supera il limite di 1 MiB.");
            totalDeclared = checked(totalDeclared + inf.DeclaredSize);
            if (totalDeclared > MaxExtractedInfBytes)
                throw new InvalidDataException("Somma INF CAB oltre il limite di 24 MiB.");

            // Some expand.exe versions flatten the directory structure. Only
            // accept CABs with unique INF basenames so we can map both forms.
            var basename = Path.GetFileName(inf.Path);
            if (!byBaseName.TryAdd(basename, inf) || !byPath.TryAdd(inf.Path, inf))
                throw new InvalidDataException(
                    "INF con nome ambiguo nel CAB: impossibile associare i file in sicurezza.");
        }

        if (infFiles.Length == 0)
            return new LocalOemDriverComparison.Report(0, devices.Count, [], 0,
                ["CAB analizzato: nessun INF dichiarato; altri file non sono stati estratti. " +
                 "Non significa che sul PC non siano disponibili aggiornamenti."]);

        var expandExecutable = Path.Combine(
            Environment.SystemDirectory, "expand.exe");
        if (!File.Exists(expandExecutable) ||
            (File.GetAttributes(expandExecutable) & FileAttributes.ReparsePoint) != 0)
            throw new FileNotFoundException("Utilità Windows expand.exe non disponibile.");

        var temporary = Directory.CreateTempSubdirectory("OmegaDrive-CabInf-").FullName;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(expandExecutable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = temporary,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            // The wildcard is fixed and trusted. CAB filenames are never
            // spliced into command syntax or passed to cmd.exe.
            start.ArgumentList.Add(fullPath);
            start.ArgumentList.Add("-F:*.inf");
            start.ArgumentList.Add(temporary);

            using var process = Process.Start(start) ??
                throw new InvalidOperationException("Impossibile avviare expand.exe.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
                if (!cancellationToken.IsCancellationRequested)
                    throw new TimeoutException("Analisi CAB oltre il limite di tempo.");
                throw;
            }

            if (process.ExitCode != 0)
                throw new InvalidDataException(
                    "Windows non riesce a espandere gli INF del CAB (formato o contenuto non supportato).");

            // Build a validated, bounded in-memory ZIP of INF only so the
            // existing PnP and platform/version checks are exactly shared.
            using var verifiedZip = new MemoryStream();
            using (var zip = new ZipArchive(
                verifiedZip, ZipArchiveMode.Create, leaveOpen: true))
            {
                var files = EnumerateSafeFiles(temporary);
                if (files.Count != infFiles.Length)
                    throw new InvalidDataException(
                        "Gli INF estratti non corrispondono all'indice CAB.");

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(temporary, file)
                        .Replace('\\', '/');
                    if (!byPath.TryGetValue(relative, out var original) &&
                        !byBaseName.TryGetValue(Path.GetFileName(relative), out original))
                        throw new InvalidDataException(
                            "expand.exe ha restituito un file non dichiarato nel CAB.");
                    if (!seen.Add(original.Path))
                        throw new InvalidDataException("INF CAB estratto più volte.");

                    var result = new FileInfo(file);
                    if (result.Length != original.DeclaredSize ||
                        result.Length > OemZipInfEvidence.MaxInfBytes)
                        throw new InvalidDataException("Dimensione INF CAB non corrispondente.");
                    total = checked(total + result.Length);
                    if (total > MaxExtractedInfBytes)
                        throw new InvalidDataException("Output CAB oltre il limite.");

                    var entry = zip.CreateEntry(original.Path, CompressionLevel.NoCompression);
                    using var destination = entry.Open();
                    using var input = new FileStream(
                        file, FileMode.Open, FileAccess.Read, FileShare.Read,
                        8192, FileOptions.SequentialScan);
                    var bytes = new byte[8192];
                    long copied = 0;
                    int count;
                    while ((count = input.Read(bytes, 0, bytes.Length)) != 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        copied = checked(copied + count);
                        if (copied > original.DeclaredSize ||
                            copied > OemZipInfEvidence.MaxInfBytes)
                            throw new InvalidDataException(
                                "INF CAB è cambiato durante la lettura.");
                        destination.Write(bytes, 0, count);
                    }
                    if (copied != original.DeclaredSize)
                        throw new InvalidDataException(
                            "INF CAB incompleto durante la lettura.");
                }

                if (seen.Count != infFiles.Length)
                    throw new InvalidDataException("INF CAB mancanti dopo la decompressione.");
            }

            verifiedZip.Position = 0;
            var compared = LocalOemDriverComparison.Compare(
                verifiedZip, devices, architecture, osMajor, osMinor, osBuild);
            return compared with
            {
                Notes = compared.Notes.Concat(
                [
                    "CAB analizzato con Windows expand.exe: solo INF temporanei. " +
                    "Gli INF sono stati eliminati dopo il confronto; " +
                    "firmatari CAT, integrità del pacchetto e ranking Windows NON verificati."
                ]).ToArray()
            };
        }
        finally
        {
            try
            {
                Directory.Delete(temporary, recursive: true);
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException)
            {
                // Never execute files. Cleanup can fail while AV/Indexing
                // holds a handle; no persistent OmegaDrive state is written.
            }
        }
    }

    private static IReadOnlyList<string> EnumerateSafeFiles(string root)
    {
        var stack = new Stack<(string Directory, int Depth)>();
        stack.Push((root, 0));
        var files = new List<string>();
        var visited = 0;
        while (stack.Count > 0)
        {
            var (directory, depth) = stack.Pop();
            if (depth > MaxFolderDepth)
                throw new InvalidDataException("CAB ha una struttura di cartelle troppo profonda.");

            foreach (var path in Directory.EnumerateFileSystemEntries(
                directory, "*", SearchOption.TopDirectoryOnly))
            {
                if (++visited > MaxOutputItems)
                    throw new InvalidDataException("Troppi file o cartelle CAB.");
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Link nel contenuto CAB non consentito.");

                if ((attrs & FileAttributes.Directory) != 0)
                {
                    stack.Push((path, depth + 1));
                    continue;
                }

                if (!path.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) ||
                    files.Count >= OemZipInfEvidence.MaxInfFiles)
                    throw new InvalidDataException(
                        "File non INF o numero eccessivo nell'output CAB.");
                files.Add(path);
            }
        }

        return files;
    }
}
