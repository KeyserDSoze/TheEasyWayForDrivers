using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Local-only, bounded plan for a downloaded OEM driver directory.

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
    public const int MaximumExeProbes = 24;
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
        // Basenames only, strictly in process memory; never formatted
        // into user-facing summaries. Reconciliation releases only names
        // that were already sanitized in the saved HTML snapshot.
        public IReadOnlyList<string> UnsupportedPackageNames { get; init; } = [];
    }

    /// <summary>OEM files are selected explicitly by the user: no SMBIOS
    /// manufacturer or cloud portal credential is required.</summary>
    public static bool CanOfferImport(bool scanCompleted,
        bool inventoryComplete, int pnpDevices) =>
        scanCompleted && inventoryComplete && pnpDevices is > 0 and <= 2048;

    public static Plan Create(string folder,
        CancellationToken cancellationToken = default,
        bool allowUnsupportedOnly = false)
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
        var unsupportedNames = new List<string>();
        var visitedDirectories = 0;
        var visitedFiles = 0;
        var exeProbes = 0;
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

            // FileSystem enumeration order varies by volume and can starve
            // useful PE-ZIP packages after 24 probes. Sort each directory
            // within the existing global file/directory bounds rather than
            // buffering an arbitrarily large untrusted listing.
            var entryLimit = MaximumVisitedFiles + MaximumDirectories;
            var entries = Directory.EnumerateFileSystemEntries(current)
                .Take(entryLimit + 1).ToArray();
            if (entries.Length > entryLimit)
                throw new InvalidDataException(
                    "Troppi elementi nella cartella OEM: importazione bloccata.");
            Array.Sort(entries, StringComparer.Ordinal);
            foreach (var path in entries)
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

                if (extension is ".msi" or ".msix" or ".7z" or
                    ".rar" or ".iso")
                {
                    // Proprietary installers remain outside the read-only
                    // INF comparison, without implying no driver exists.
                    unsupported++;
                    unsupportedNames.Add(Path.GetFileName(path));
                    continue;
                }

                if (extension is not (".zip" or ".cab" or ".exe"))
                    continue;

                var file = new FileInfo(path);
                var maximum = extension == ".zip"
                    ? OemArchiveInspector.MaxZipArchiveBytes
                    : OemArchiveInspector.MaxArchiveBytes;

                if (extension == ".exe")
                {
                    // A PE with a real standard ZIP overlay is the ONLY
                    // supported EXE shape, already checked by the strict
                    // existing no-execution reader. Unsupported EXE formats
                    // are explicitly counted, never silently accepted.
                    // Cap probing so a directory of installers cannot
                    // force thousands of 16MiB header searches.
                    // Too-small/oversize/missing EXEs cannot be probed as
                    // PE-ZIP. Keep the coverage gap, but do not spend one of
                    // the 24 expensive overlay scans on them.
                    if (!file.Exists || file.Length < 128 ||
                        file.Length > maximum)
                    {
                        unsupported++;
                        unsupportedNames.Add(Path.GetFileName(path));
                        continue;
                    }
                    if (++exeProbes > MaximumExeProbes)
                    {
                        unsupported++;
                        unsupportedNames.Add(Path.GetFileName(path));
                        continue;
                    }

                    try
                    {
                        using var stream = new FileStream(
                            path, FileMode.Open, FileAccess.Read,
                            FileShare.Read, 65536, FileOptions.SequentialScan);
                        using var overlay = OemPeZipOverlay.Open(stream);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    catch (NotSupportedException)
                    {
                        // PE with proprietary 7z/NSIS/Inno payload or no
                        // ZIP overlay; preserve an explicit coverage gap.
                        unsupported++;
                        unsupportedNames.Add(Path.GetFileName(path));
                        continue;
                    }
                    // Malformed PE/unsafe ZIP throws InvalidDataException:
                    // fail the WHOLE import rather than skip a dangerous
                    // package and report an apparently complete result.
                }
                else if (!file.Exists || file.Length is < 4 ||
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
                        "Oltre 24 sorgenti ZIP/CAB/PE-ZIP/INF: suddividere il catalogo.");
            }
        }

        if (archives.Count + (looseInf ? 1 : 0) > MaximumSources)
            throw new InvalidDataException(
                "Oltre 24 sorgenti ZIP/CAB/PE-ZIP/INF: suddividere il catalogo.");
        // A saved, user-imported HTML list may still be reconciled against
        // proprietary packages when NO inspectable INF/archive exists.
        // Do not enable this for the ordinary folder-only comparison,
        // and do not treat an empty folder as a successful import.
        if (!looseInf && archives.Count == 0 &&
            (!allowUnsupportedOnly || unsupported == 0))
            throw new InvalidDataException(
                "Nessun INF, ZIP, CAB o EXE con ZIP incorporato analizzabile. " +
                "EXE/MSI/7z proprietari non vengono eseguiti e " +
                "NON provano assenza di driver.");

        // Deterministic source numbering; paths are never displayed or
        // copied to a diagnostic report (may contain private order IDs).
        archives.Sort(StringComparer.OrdinalIgnoreCase);
        return new Plan(root, archives, looseInf, unsupported,
            visitedFiles, visitedDirectories)
        {
            UnsupportedPackageNames = unsupportedNames
        };
    }
}
