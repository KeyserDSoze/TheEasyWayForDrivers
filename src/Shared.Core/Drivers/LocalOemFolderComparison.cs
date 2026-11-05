using System.IO.Compression;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Read-only comparison of an existing driver directory against Windows PnP.
/// Does not execute installers or copy any binaries. Only explicitly bounded
/// INF files enter an in-memory ZIP for the existing platform/ID/version
/// preflight. A matching INF never becomes an installable update.
/// </summary>
public static class LocalOemFolderComparison
{
    public const int MaxDirectories = 512;
    public const int MaxDepth = 16;

    public static LocalOemDriverComparison.Report Compare(
        string directoryPath,
        IReadOnlyList<DriverInfo> installed,
        string architecture,
        int osMajor,
        int osMinor,
        int osBuild,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(installed);
        if (installed.Count > 2048)
            throw new InvalidDataException("Troppi dispositivi per la comparazione.");

        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(directoryPath);
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists ||
            (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                "La cartella scelta non esiste o è un collegamento non consentito.");

        // Never follow filesystem links. The caller explicitly selects the
        // root folder; we neither run OEM setup.exe nor recurse outside it.
        var directories = new Stack<(string Path, int Depth)>();
        directories.Push((root, 0));
        var visited = 0;
        var queuedDirectories = 1;
        var infCount = 0;
        long totalInfBytes = 0;

        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            while (directories.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (current, depth) = directories.Pop();
                if (depth > MaxDepth || ++visited > MaxDirectories)
                    throw new InvalidDataException(
                        "Troppe cartelle o struttura driver troppo profonda.");

                var currentAttributes = File.GetAttributes(current);
                if ((currentAttributes & FileAttributes.ReparsePoint) != 0 ||
                    (currentAttributes & FileAttributes.Directory) == 0)
                    throw new InvalidDataException(
                        "Collegamento o cartella driver non valida.");

                // Stream paths instead of sorting/materializing an unbounded
                // directory listing before the 96-INF safety cap can apply.
                foreach (var file in Directory.EnumerateFiles(
                    current, "*.inf", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Win32 wildcard semantics may also return names
                    // matching short-name aliases. Read actual .inf only.
                    if (!Path.GetExtension(file).Equals(".inf", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (++infCount > OemZipInfEvidence.MaxInfFiles)
                        throw new InvalidDataException(
                            "La cartella contiene troppi INF per una verifica sicura.");

                    var info = new FileInfo(file);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        info.Length > OemZipInfEvidence.MaxInfBytes)
                        throw new InvalidDataException(
                            "INF collegato o oltre il limite di 1 MiB.");

                    totalInfBytes = checked(totalInfBytes + info.Length);
                    if (totalInfBytes > OemZipInfEvidence.MaxTotalInfBytes)
                        throw new InvalidDataException(
                            "Gli INF superano il limite totale di 24 MiB.");

                    // Generate a safe, uniquely keyed archive path without
                    // retaining or exporting absolute paths, usernames or
                    // manufacturer order numbers. The basename is informative.
                    var basename = Path.GetFileNameWithoutExtension(file);
                    var safeName = new string(basename.Take(72).Select(c =>
                        char.IsLetterOrDigit(c) ||
                        c is '_' or '-' or '+' or '(' or ')' or '.'
                            ? c : '_').ToArray());
                    if (string.IsNullOrWhiteSpace(safeName) || safeName is "." or "..")
                        safeName = "driver";
                    var entry = zip.CreateEntry(
                        $"d{infCount:D3}/{safeName}.inf",
                        CompressionLevel.NoCompression);

                    using var source = new FileStream(
                        file, FileMode.Open, FileAccess.Read, FileShare.Read,
                        8192, FileOptions.SequentialScan);
                    using var output = entry.Open();
                    var buffer = new byte[8192];
                    long copied = 0;
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        copied = checked(copied + read);
                        if (copied > OemZipInfEvidence.MaxInfBytes ||
                            copied > info.Length)
                            throw new InvalidDataException(
                                "L'INF è cambiato o supera il limite durante la lettura.");
                        output.Write(buffer, 0, read);
                    }
                    if (copied != info.Length)
                        throw new InvalidDataException(
                            "L'INF è cambiato durante il confronto.");
                }

                foreach (var child in Directory.EnumerateDirectories(
                    current, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException(
                            "Collegamento/junction nella cartella OEM: confronto bloccato.");
                    if ((attributes & FileAttributes.Directory) == 0)
                        throw new InvalidDataException("Elemento directory non valido.");
                    if (depth >= MaxDepth)
                        throw new InvalidDataException(
                            "Profondità massima delle cartelle driver superata.");
                    if (++queuedDirectories > MaxDirectories)
                        throw new InvalidDataException(
                            "Troppi sottopercorsi nella cartella OEM.");
                    directories.Push((child, depth + 1));
                }
            }
        }

        if (infCount == 0)
            return new LocalOemDriverComparison.Report(
                0, installed.Count, [], 0,
                ["Nessun INF presente nella cartella selezionata. " +
                 "Non significa che non esistano driver nuovi: " +
                 "l'installer o il formato OEM potrebbe non essere stato estratto."]);

        memory.Position = 0;
        var report = LocalOemDriverComparison.Compare(
            memory, installed, architecture, osMajor, osMinor, osBuild);
        return report with
        {
            Notes = report.Notes.Concat(
            [
                "Cartella OEM letta in sola lettura. Nessun EXE, DLL o " +
                "installatore eseguito o caricato; solo INF in memoria. " +
                "Firma digitale, origine e applicabilità SetupAPI NON verificate."
            ]).ToArray()
        };
    }
}
