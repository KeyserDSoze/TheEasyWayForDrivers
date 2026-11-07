using System.IO.Compression;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Two bounded read-only nesting levels for OEM ZIP packages containing
/// other ZIP/CAB driver packs. CAB uses the already restricted Windows
/// expand.exe -> INF-only pipeline. No EXE/MSI is ever launched or unpacked.
/// A bad child rejects the entire package, not a fabricated "zero drivers".
/// </summary>
public static class LocalOemNestedArchiveComparison
{
    // Global limits across BOTH nested levels of a single outer package.
    // A per-folder limit could otherwise allow 8 x 8 child archives.
    public const int MaximumInnerArchives = 8;
    public const int MaximumNestedDepth = 2;
    public const long MaximumTotalInnerBytes = 1024L * 1024 * 1024;

    private sealed class TraversalBudget
    {
        public int ReservedChildren { get; private set; }
        public int NextStagingIndex { get; set; }
        public int UninspectedDeeperChildren { get; set; }
        public long ReservedBytes { get; private set; }

        public void Reserve(IReadOnlyList<OemArchiveInspector.Entry> children)
        {
            if (children.Count > MaximumInnerArchives - ReservedChildren)
                throw new InvalidDataException(
                    "Troppi ZIP/CAB annidati nell'intero pacchetto OEM.");

            var bytes = ReservedBytes;
            foreach (var child in children)
            {
                if (child.DeclaredBytes is < 4 or > OemArchiveInspector.MaxEntryBytes)
                    throw new InvalidDataException(
                        "Dimensioni archivio annidato fuori dai limiti di sicurezza.");
                bytes = checked(bytes + child.DeclaredBytes);
                if (bytes > MaximumTotalInnerBytes)
                    throw new InvalidDataException(
                        "Dimensioni ZIP/CAB annidati complessive oltre 1 GiB.");
            }

            // Reserve each sibling BEFORE any extraction. Descendants share
            // this same budget, including across separate outer branches.
            ReservedChildren += children.Count;
            ReservedBytes = bytes;
        }
    }

    public static async Task<LocalOemDriverComparison.Report> CompareAsync(
        Stream outerZip, IReadOnlyList<DriverInfo> devices,
        string architecture, int osMajor, int osMinor, int osBuild,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outerZip);
        ArgumentNullException.ThrowIfNull(devices);
        cancellationToken.ThrowIfCancellationRequested();
        if (!outerZip.CanRead || !outerZip.CanSeek)
            throw new InvalidDataException("Archivio OEM non leggibile.");

        outerZip.Position = 0;
        var directory = OemArchiveInspector.Inspect(outerZip);
        if (directory.ArchiveFormat != OemArchiveInspector.Format.Zip)
            throw new NotSupportedException("La ricerca annidata richiede uno ZIP reale.");

        // Preserve the established, identical report for direct-INF ZIPs.
        if (!directory.Entries.Any(entry => IsNestedArchive(entry.Path)))
        {
            outerZip.Position = 0;
            return LocalOemDriverComparison.Compare(
                outerZip, devices, architecture, osMajor, osMinor, osBuild);
        }

        var budget = new TraversalBudget();
        var reports = new List<LocalOemDriverComparison.Report>(
            MaximumInnerArchives + 1);
        // Only generated staging names ever touch the filesystem. Original
        // OEM entry paths and PnP identities are not written to reports.
        var folder = Directory.CreateTempSubdirectory("OmegaDrive-NestedInf-").FullName;
        try
        {
            await InspectZipTreeAsync(
                outerZip, devices, architecture, osMajor, osMinor, osBuild,
                depth: 0, folder, budget, reports, cancellationToken);
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
                // Windows antivirus/indexing can temporarily hold files.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var summary = LocalOemArchiveBatchSummary.Combine(reports);
        var uniqueOrdinals = reports
            .SelectMany(report => report.MatchedDeviceOrdinals ??
                throw new InvalidDataException(
                    "Identità PnP di un archivio OEM non verificabile."))
            .Distinct().OrderBy(x => x).ToArray();
        if (summary.DistinctMatchedDevices != uniqueOrdinals.Length)
            throw new InvalidDataException(
                "Conteggio PnP non coerente fra i pacchetti annidati.");

        var omittedWithin = reports.Sum(report =>
            Math.Max(0, report.AdditionalMatchesOmitted -
                report.MatchingDevicesOmitted));
        var notes = new List<string>
        {
            $"Pacchetto OEM: ZIP principale + {budget.ReservedChildren} archivi ZIP/CAB " +
            "interni esaminati (fino a due livelli); nessun EXE eseguito.",
            "Dispositivi correlati per indice PnP temporaneo: " +
            "gli stessi device in più archivi vengono mostrati una sola volta.",
            "Nomi originali dei file ZIP/CAB/INF e identificativi PnP " +
            "non riportati. CAT/firme e ranking Windows NON verificati."
        };
        if (budget.UninspectedDeeperChildren > 0)
            notes.Add($"{budget.UninspectedDeeperChildren} ZIP/CAB oltre il secondo " +
                "livello NON analizzati: la copertura del pacchetto resta parziale.");
        if (reports.Any(report => report.RejectedInfCount > 0 ||
            report.MatchingDevicesOmitted > 0))
            notes.Add("Alcune evidenze INF sono state scartate o limitate: " +
                "il risultato non è una prova di assenza driver.");

        var duplicateAssociations = summary.RepeatedAssociations ?? 0;
        return new LocalOemDriverComparison.Report(
            reports.Sum(report => report.InfExamined),
            devices.Count,
            summary.Findings,
            checked(omittedWithin + duplicateAssociations + summary.RowsOmitted),
            notes,
            reports.Sum(report => report.RejectedInfCount),
            reports.Sum(report => report.InfWithDeclaredIds),
            reports.Sum(report => report.LegacyAnsiCount),
            summary.RowsOmitted)
        {
            MatchedDeviceOrdinals = uniqueOrdinals,
            HasUninspectedNestedArchives =
                budget.UninspectedDeeperChildren > 0
        };
    }

    private static async Task InspectZipTreeAsync(
        Stream zip, IReadOnlyList<DriverInfo> devices,
        string architecture, int osMajor, int osMinor, int osBuild,
        int depth, string folder, TraversalBudget budget,
        List<LocalOemDriverComparison.Report> reports,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        zip.Position = 0;
        var directory = OemArchiveInspector.Inspect(zip);
        if (directory.ArchiveFormat != OemArchiveInspector.Format.Zip)
            throw new InvalidDataException("ZIP OEM annidato non valido.");

        var children = directory.Entries
            .Where(item => IsNestedArchive(item.Path))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (depth == MaximumNestedDepth)
            budget.UninspectedDeeperChildren =
                checked(budget.UninspectedDeeperChildren + children.Length);
        else
            budget.Reserve(children);

        zip.Position = 0;
        reports.Add(LocalOemDriverComparison.Compare(
            zip, devices, architecture, osMajor, osMinor, osBuild));

        if (depth == MaximumNestedDepth || children.Length == 0)
            return;

        zip.Position = 0;
        using var archive = new ZipArchive(
            zip, ZipArchiveMode.Read, leaveOpen: true);
        var byName = OemArchiveInspector.CreateValidatedZipEntryIndex(archive);
        var chunk = new byte[64 * 1024];
        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byName.TryGetValue(child.Path, out var entry))
                throw new InvalidDataException(
                    "Archivio annidato non più presente nell'indice ZIP.");
            if (entry.Length != child.DeclaredBytes)
                throw new InvalidDataException(
                    "Dimensione archivio annidato mutata durante la lettura.");

            var extension = Path.GetExtension(child.Path).ToLowerInvariant();
            var staged = Path.Combine(folder,
                $"inner-{budget.NextStagingIndex++:D2}{extension}");
            try
            {
                await using (var destination = new FileStream(
                    staged, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 64 * 1024, FileOptions.SequentialScan))
                await using (var input = entry.Open())
                {
                    long copied = 0;
                    int count;
                    while ((count = await input.ReadAsync(
                        chunk, cancellationToken)) != 0)
                    {
                        copied = checked(copied + count);
                        if (copied > child.DeclaredBytes ||
                            copied > OemArchiveInspector.MaxEntryBytes)
                            throw new InvalidDataException(
                                "Archivio OEM annidato oltre i limiti.");
                        await destination.WriteAsync(
                            chunk.AsMemory(0, count), cancellationToken);
                    }
                    if (copied != child.DeclaredBytes)
                        throw new InvalidDataException(
                            "Archivio OEM annidato troncato.");
                }

                if (extension == ".cab")
                {
                    // Only the restricted System32 expand.exe -> INF path,
                    // after CAB index validation. Never launch OEM binaries.
                    reports.Add(await LocalOemCabComparison.CompareAsync(
                        staged, devices, architecture,
                        osMajor, osMinor, osBuild, cancellationToken));
                }
                else
                {
                    using var childZip = new FileStream(
                        staged, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 64 * 1024,
                        FileOptions.SequentialScan);
                    await InspectZipTreeAsync(
                        childZip, devices, architecture,
                        osMajor, osMinor, osBuild, depth + 1, folder,
                        budget, reports, cancellationToken);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(staged))
                        File.Delete(staged);
                }
                catch (Exception error) when (
                    error is IOException or UnauthorizedAccessException)
                {
                    // A locked staging file is retried by folder cleanup.
                }
            }
        }
    }

    private static bool IsNestedArchive(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cab", StringComparison.OrdinalIgnoreCase);
    }
}
