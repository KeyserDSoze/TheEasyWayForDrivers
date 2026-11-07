using System.IO.Compression;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// A single, bounded read-only nesting level for OEM ZIP packages containing
/// other ZIP/CAB driver packs. CAB uses the already restricted Windows
/// expand.exe -> INF-only pipeline. No EXE/MSI is ever launched or unpacked.
/// A bad child rejects the entire package, not a fabricated "zero drivers".
/// </summary>
public static class LocalOemNestedArchiveComparison
{
    public const int MaximumInnerArchives = 8;
    public const long MaximumTotalInnerBytes = 1024L * 1024 * 1024;

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

        // Never inspect arbitrary .exe, .msi, 7z or application binaries as
        // if their compressed payload had an authenticated driver identity.
        var children = directory.Entries
            .Where(entry => IsNestedArchive(entry.Path))
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (children.Length == 0)
        {
            outerZip.Position = 0;
            return LocalOemDriverComparison.Compare(
                outerZip, devices, architecture, osMajor, osMinor, osBuild);
        }

        if (children.Length > MaximumInnerArchives)
            throw new InvalidDataException(
                "Troppi ZIP/CAB annidati: il pacchetto OEM non è stato verificato.");
        long declared = 0;
        foreach (var item in children)
        {
            if (item.DeclaredBytes is < 4 or > OemArchiveInspector.MaxEntryBytes)
                throw new InvalidDataException(
                    "Dimensioni archivio annidato fuori dai limiti di sicurezza.");
            declared = checked(declared + item.DeclaredBytes);
            if (declared > MaximumTotalInnerBytes)
                throw new InvalidDataException(
                    "Dimensioni ZIP/CAB annidati complessive oltre 1 GiB.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        outerZip.Position = 0;
        var reports = new List<LocalOemDriverComparison.Report>(children.Length + 1)
        {
            // Read direct outer INF as before, even when an inner child also
            // describes the same PnP device. These reports are correlated
            // using ephemeral inventory ordinals, NOT the friendly name.
            LocalOemDriverComparison.Compare(
                outerZip, devices, architecture, osMajor, osMinor, osBuild)
        };

        // No original archive entry names/paths are included in the report.
        // Only bounded, system-generated .zip/.cab staging names are used.
        var folder = Directory.CreateTempSubdirectory("OmegaDrive-NestedInf-").FullName;
        var deeper = 0;
        try
        {
            outerZip.Position = 0;
            using var archive = new ZipArchive(
                outerZip, ZipArchiveMode.Read, leaveOpen: true);
            var byName = archive.Entries.ToDictionary(
                entry => entry.FullName.Replace('\\', '/'),
                StringComparer.OrdinalIgnoreCase);
            var chunk = new byte[64 * 1024];
            for (var index = 0; index < children.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var child = children[index];
                if (!byName.TryGetValue(child.Path, out var entry))
                    throw new InvalidDataException(
                        "Archivio annidato non più presente nell'indice ZIP.");
                if (entry.Length != child.DeclaredBytes)
                    throw new InvalidDataException(
                        "Dimensione archivio annidato mutata durante la lettura.");

                var extension = Path.GetExtension(child.Path).ToLowerInvariant();
                var staged = Path.Combine(folder,
                    $"inner-{index:D2}{extension}");
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

                    LocalOemDriverComparison.Report report;
                    if (extension == ".cab")
                    {
                        // Existing CAB path validates its CFFILE directory
                        // before calling ONLY Windows System32 expand.exe
                        // for individual INF files into a private folder.
                        report = await LocalOemCabComparison.CompareAsync(
                            staged, devices, architecture,
                            osMajor, osMinor, osBuild, cancellationToken);
                    }
                    else
                    {
                        using var childZip = new FileStream(
                            staged, FileMode.Open, FileAccess.Read,
                            FileShare.Read, 64 * 1024,
                            FileOptions.SequentialScan);
                        var nestedDirectory = OemArchiveInspector.Inspect(childZip);
                        deeper += nestedDirectory.Entries.Count(item =>
                            IsNestedArchive(item.Path));
                        childZip.Position = 0;
                        report = LocalOemDriverComparison.Compare(
                            childZip, devices, architecture,
                            osMajor, osMinor, osBuild);
                    }

                    // Even an empty inner CAB must supply an exact empty
                    // inventory correlation, not legacy/unknown identity.
                    reports.Add(report);
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
                        // This file is never executable by OmegaDrive. The
                        // folder cleanup below is attempted independently.
                    }
                }
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
            $"Pacchetto OEM: ZIP principale + {children.Length} archivi ZIP/CAB " +
            "interni esaminati (un solo livello); nessun EXE eseguito.",
            "Dispositivi correlati per indice PnP temporaneo: " +
            "gli stessi device in più archivi vengono mostrati una sola volta.",
            "Nomi originali dei file ZIP/CAB/INF e identificativi PnP " +
            "non riportati. CAT/firme e ranking Windows NON verificati."
        };
        if (deeper > 0)
            notes.Add($"{deeper} ZIP/CAB di secondo livello NON analizzati: " +
                "la copertura del pacchetto resta parziale.");
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
            MatchedDeviceOrdinals = uniqueOrdinals
        };
    }

    private static bool IsNestedArchive(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cab", StringComparison.OrdinalIgnoreCase);
    }
}
