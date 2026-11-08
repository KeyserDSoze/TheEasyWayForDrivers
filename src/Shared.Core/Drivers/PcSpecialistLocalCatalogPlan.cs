using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Local-only, bounded plan for a downloaded PCSpecialist driver directory.
/// Source names/paths are kept IN MEMORY for the caller and never formatted
/// into the user-facing report. The selected folder is NOT proof of
/// authenticity, applicability, CAT signature or a downloadable update.
/// </summary>
public static class PcSpecialistLocalCatalogPlan
{
    public const int MaximumDirectories = LocalOemFolderComparison.MaxDirectories;
    public const int MaximumDepth = LocalOemFolderComparison.MaxDepth;
    public const int MaximumVisitedFiles = 3000;
    public const int MaximumSources = LocalOemArchiveBatchSummary.MaximumArchives;
    public const long MaximumArchiveBytes =
        LocalOemArchiveBatchSummary.MaximumTotalArchiveBytes;

    public sealed record Plan(
        string RootPath,
        IReadOnlyList<string> Archives,
        bool HasLooseInf,
        int UnsupportedPackages,
        int FilesVisited,
        int DirectoriesVisited)
    {
        public int SourceCount => Archives.Count + (HasLooseInf ? 1 : 0);
        public bool IsPartial => UnsupportedPackages > 0;
        public bool IsInstallable => false;
        public bool IsVerifiedOfficialSource => false;
    }

    public static Plan Create(string folder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        cancellationToken.ThrowIfCancellationRequested();

        var root = Path.GetFullPath(folder);
        var info = new DirectoryInfo(root);
        if (!info.Exists ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                "La cartella driver locale non esiste oppure è un collegamento.");

        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var archives = new List<string>();
        var looseInf = false;
        var unsupported = 0;
        var visitedDirectories = 0;
        var visitedFiles = 0;
        long totalArchiveBytes = 0;

        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, depth) = pending.Pop();
            if (++visitedDirectories > MaximumDirectories ||
                depth > MaximumDepth)
                throw new InvalidDataException(
                    "Cartella driver troppo ampia o profonda per una verifica completa.");

            var directoryAttributes = File.GetAttributes(current);
            if ((directoryAttributes & FileAttributes.ReparsePoint) != 0 ||
                (directoryAttributes & FileAttributes.Directory) == 0)
                throw new InvalidDataException(
                    "Junction/collegamento nella cartella OEM: importazione bloccata.");

            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        "Collegamento/reparse nella cartella OEM: importazione bloccata.");

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (depth >= MaximumDepth ||
                        pending.Count + visitedDirectories >= MaximumDirectories)
                        throw new InvalidDataException(
                            "Numero o profondità di sottocartelle OEM oltre limite.");
                    pending.Push((path, depth + 1));
                    continue;
                }

                if (++visitedFiles > MaximumVisitedFiles)
                    throw new InvalidDataException(
                        "Troppi file nella cartella: importazione non esaustiva.");

                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".inf")
                {
                    looseInf = true;
                    continue;
                }

                if (extension is ".exe" or ".msi" or ".msix" or ".7z" or
                    ".rar" or ".iso")
                {
                    // Proprietary EXE/7z/MSI are NOT a driver catalogue
                    // without explicit local signature/package validation.
                    // Never execute and never call them confirmed updates.
                    unsupported++;
                    continue;
                }

                if (extension is not (".zip" or ".cab"))
                    continue;

                var file = new FileInfo(path);
                var maximum = extension == ".zip"
                    ? OemArchiveInspector.MaxZipArchiveBytes
                    : OemArchiveInspector.MaxArchiveBytes;
                if (!file.Exists || file.Length is < 4 ||
                    file.Length > maximum)
                    throw new InvalidDataException(
                        "Archivio ZIP/CAB mancante o dimensioni non verificabili.");

                totalArchiveBytes = checked(totalArchiveBytes + file.Length);
                if (totalArchiveBytes > MaximumArchiveBytes)
                    throw new InvalidDataException(
                        "Pacchetti OEM oltre 4 GiB complessivi.");
                archives.Add(path);
                if (archives.Count + (looseInf ? 1 : 0) > MaximumSources)
                    throw new InvalidDataException(
                        "Oltre 12 sorgenti ZIP/CAB/INF: suddividere il catalogo.");
            }
        }

        if (archives.Count + (looseInf ? 1 : 0) > MaximumSources)
            throw new InvalidDataException(
                "Oltre 12 sorgenti ZIP/CAB/INF: suddividere il catalogo.");
        if (!looseInf && archives.Count == 0)
            throw new InvalidDataException(
                "Nessun INF, ZIP o CAB analizzabile. EXE/MSI/7z " +
                "non vengono eseguiti e NON provano assenza di driver.");

        // Deterministic source numbering; paths are never displayed or
        // copied to a diagnostic report (may contain private order IDs).
        archives.Sort(StringComparer.OrdinalIgnoreCase);
        return new Plan(root, archives, looseInf, unsupported,
            visitedFiles, visitedDirectories);
    }
}
