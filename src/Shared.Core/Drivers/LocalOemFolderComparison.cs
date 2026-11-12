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
    public const int MaxInfBatches = 4;
    public const int MaxFolderInfFiles = OemZipInfEvidence.MaxInfFiles * MaxInfBatches;

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

        // Preserve each archive parser's 96-INF / 24-MiB safety ceiling:
        // stream the selected folder through at most four independent,
        // disposable ZIP batches. No driver binaries or EXEs are copied.
        var directories = new Stack<(string Path, int Depth)>();
        directories.Push((root, 0));
        var visited = 0;
        var queuedDirectories = 1;
        var infCount = 0;
        var currentInfCount = 0;
        long currentInfBytes = 0;
        long totalInfBytes = 0;
        var batches = new List<LocalOemDriverComparison.Report>(MaxInfBatches);
        MemoryStream memory = new();
        ZipArchive zip = new(memory, ZipArchiveMode.Create, leaveOpen: true);

        void FlushBatch()
        {
            if (currentInfCount == 0)
                return;
            cancellationToken.ThrowIfCancellationRequested();
            // The ZIP central directory must be complete before reading.
            zip.Dispose();
            memory.Position = 0;
            batches.Add(LocalOemDriverComparison.Compare(
                memory, installed, architecture, osMajor, osMinor, osBuild));
            memory.Dispose();

            // This fresh empty archive is disposed by the outer finally.
            memory = new MemoryStream();
            zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true);
            currentInfCount = 0;
            currentInfBytes = 0;
        }

        try
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

                // Do not eagerly materialize unbounded filesystem listings.
                foreach (var file in Directory.EnumerateFiles(
                    current, "*.inf", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Path.GetExtension(file).Equals(".inf",
                        StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (++infCount > MaxFolderInfFiles)
                        throw new InvalidDataException(
                            "Oltre 384 INF nella cartella: verifica non completata.");

                    var info = new FileInfo(file);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        info.Length > OemZipInfEvidence.MaxInfBytes)
                        throw new InvalidDataException(
                            "INF collegato o oltre il limite di 4 MiB.");

                    totalInfBytes = checked(totalInfBytes + info.Length);
                    if (totalInfBytes >
                        (long)OemZipInfEvidence.MaxTotalInfBytes * MaxInfBatches)
                        throw new InvalidDataException(
                            "INF oltre il limite globale di 96 MiB.");

                    // A single batch is limited by BOTH number and bytes;
                    // each remains fully validated by the existing ZIP reader.
                    if (currentInfCount == OemZipInfEvidence.MaxInfFiles ||
                        currentInfBytes + info.Length >
                            OemZipInfEvidence.MaxTotalInfBytes)
                    {
                        FlushBatch();
                    }

                    if (batches.Count >= MaxInfBatches)
                        throw new InvalidDataException(
                            "Troppi lotti INF per il confronto OEM.");

                    currentInfCount++;
                    currentInfBytes += info.Length;

                    // A sanitized original basename is NOT anonymous:
                    // OEM files can embed customer order numbers, local
                    // usernames or device serials in otherwise ASCII-safe
                    // characters. Use an entirely generated identity, unique
                    // even across batches and duplicate names in subfolders.
                    // This is the only INF path entering the ZIP parser/UI.
                    var entry = zip.CreateEntry(
                        $"d{currentInfCount:D3}/inf{infCount:D4}.inf",
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

            FlushBatch();
        }
        finally
        {
            zip.Dispose();
            memory.Dispose();
        }

        if (batches.Count == 0)
            return new LocalOemDriverComparison.Report(
                0, installed.Count, [], 0,
                ["Nessun INF presente nella cartella selezionata. " +
                 "Non significa che non esistano driver nuovi: " +
                 "l'installer o il formato OEM potrebbe non essere stato estratto."])
            {
                MatchedDeviceOrdinals = [],
                PnpMatchedDeviceOrdinals = []
            };

        const string readOnlyNote =
            "Cartella OEM letta in sola lettura. Nessun EXE, DLL o " +
            "installatore eseguito o caricato; solo INF in memoria. " +
            "Firma digitale, origine e applicabilità SetupAPI NON verificate.";

        if (batches.Count == 1)
            return batches[0] with
            {
                Notes = batches[0].Notes.Append(readOnlyNote).ToArray()
            };

        // Reuse the cross-archive ordinal-based de-duplicator. The same
        // NVIDIA/Killer/Realtek device in multiple batches is ONE PnP device,
        // not one invented driver update per INF. Its 80-row cap remains.
        var combined = LocalOemArchiveBatchSummary.Combine(batches);
        if (combined.DistinctMatchedDevices is not int distinct ||
            combined.RepeatedAssociations is not int repeated ||
            batches.Any(batch => batch.MatchedDeviceOrdinals is null))
            throw new InvalidDataException(
                "Impossibile deduplicare i dispositivi OEM fra lotti.");

        var ordinals = batches
            .SelectMany(batch => batch.MatchedDeviceOrdinals!)
            .Distinct().OrderBy(index => index).ToArray();
        if (ordinals.Length != distinct)
            throw new InvalidDataException(
                "Numero dispositivi OEM incoerente fra lotti.");
        var strongOrdinals = batches
            .SelectMany(batch => batch.PnpMatchedDeviceOrdinals ??
                throw new InvalidDataException(
                    "Evidenza PnP INF completa non verificabile."))
            .Distinct().OrderBy(index => index).ToArray();
        if (strongOrdinals.Except(ordinals).Any())
            throw new InvalidDataException(
                "Evidenza PnP INF incoerente fra lotti.");

        var omitted = Math.Max(0, distinct - combined.Findings.Count);
        var duplicateInfMatches = batches.Sum(batch =>
            batch.AdditionalMatchesOmitted - batch.MatchingDevicesOmitted);
        var notes = batches.SelectMany(batch => batch.Notes)
            .Concat(combined.Notes)
            .Append($"{batches.Count} lotti INF analizzati (max " +
                $"{OemZipInfEvidence.MaxInfFiles} INF e 24 MiB per lotto); " +
                "dispositivi deduplicati, non aggiornamenti confermati.")
            .Append(readOnlyNote)
            .Distinct().ToArray();

        return new LocalOemDriverComparison.Report(
            infCount, installed.Count, combined.Findings,
            duplicateInfMatches + repeated + omitted, notes,
            batches.Sum(batch => batch.RejectedInfCount),
            batches.Sum(batch => batch.InfWithDeclaredIds),
            batches.Sum(batch => batch.LegacyAnsiCount),
            omitted)
        {
            MatchedDeviceOrdinals = ordinals,
            PnpMatchedDeviceOrdinals = strongOrdinals,
            ExcludedAmbiguousPnpInstances =
                combined.ExcludedAmbiguousPnpInstances
        };
    }
}
