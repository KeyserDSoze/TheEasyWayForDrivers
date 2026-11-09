using System.IO.Compression;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Two bounded read-only nesting levels for OEM ZIP packages containing
/// ZIP/CAB or PE executables with a real standard ZIP overlay. CAB uses
/// restricted Windows expand.exe -> INF-only; PE bytes are NEVER executed.
/// Proprietary EXEs are an explicit coverage gap, never "zero drivers".
/// A malformed supported ZIP/PE child rejects the whole package.
/// </summary>
public static class LocalOemNestedArchiveComparison
{
    // Global limits across BOTH nested levels of a single outer package.
    // A per-folder limit could otherwise allow 8 x 8 child archives.
    public const int MaximumInnerArchives = 8;
    public const int MaximumNestedExecutableProbes = 4;
    public const int MaximumNestedDepth = 2;
    public const long MaximumTotalInnerBytes = 1024L * 1024 * 1024;

    private sealed class TraversalBudget
    {
        public int ReservedChildren { get; private set; }
        public int NextStagingIndex { get; set; }
        public int UninspectedDeeperChildren { get; set; }
        public int UninspectedExecutables { get; set; }
        public int UninspectedProprietaryPackages { get; set; }
        public int ExecutableProbes { get; set; }
        public int AcceptedPeZip { get; set; }
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
        if (!directory.Entries.Any(entry =>
            IsNestedArchive(entry.Path) || IsExecutable(entry.Path) ||
            IsUnsupportedPackage(entry.Path)))
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
        var innerTypes = budget.ExecutableProbes == 0
            ? "ZIP/CAB" : "ZIP/CAB/PE-ZIP";
        var notes = new List<string>
        {
            $"Pacchetto OEM: ZIP principale + {budget.ReservedChildren} archivi {innerTypes} " +
            "interni verificati o tentati (fino a due livelli); nessun EXE eseguito.",
            "Dispositivi correlati per indice PnP temporaneo: " +
            "gli stessi device in più archivi vengono mostrati una sola volta.",
            "Nomi originali dei file ZIP/CAB/INF e identificativi PnP " +
            "non riportati. CAT/firme e ranking Windows NON verificati."
        };
        if (budget.UninspectedDeeperChildren > 0)
            notes.Add($"{budget.UninspectedDeeperChildren} ZIP/CAB oltre il secondo " +
                "livello NON analizzati: la copertura del pacchetto resta parziale.");
        if (budget.UninspectedExecutables > 0)
            notes.Add($"{budget.UninspectedExecutables} EXE annidati proprietari, " +
                "non PE-ZIP o oltre il limite NON analizzati: copertura parziale, " +
                "non assenza di driver.");
        if (budget.UninspectedProprietaryPackages > 0)
            notes.Add($"{budget.UninspectedProprietaryPackages} MSI/MSIX/7z/RAR/ISO " +
                "nel bundle non analizzati: copertura parziale, " +
                "non assenza di driver.");
        if (budget.AcceptedPeZip > 0)
            notes.Add($"{budget.AcceptedPeZip} EXE con overlay ZIP standard " +
                "esaminati senza eseguire alcun programma.");
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
                budget.UninspectedDeeperChildren > 0 ||
                budget.UninspectedExecutables > 0 ||
                budget.UninspectedProprietaryPackages > 0,
            UninspectedNestedExecutables = budget.UninspectedExecutables,
            UninspectedDeeperArchives = budget.UninspectedDeeperChildren,
            UninspectedProprietaryPackages =
                budget.UninspectedProprietaryPackages
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

        var regular = directory.Entries
            .Where(item => IsNestedArchive(item.Path))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var executables = directory.Entries
            .Where(item => IsExecutable(item.Path))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        // MSI/7z/RAR/ISO are NEVER executed, opened or unpacked.
        // Their presence is a source coverage gap even when the outer ZIP
        // contains no readable INF or supported nested archive.
        budget.UninspectedProprietaryPackages = checked(
            budget.UninspectedProprietaryPackages +
            directory.Entries.Count(item => IsUnsupportedPackage(item.Path)));

        OemArchiveInspector.Entry[] children;
        if (depth == MaximumNestedDepth)
        {
            budget.UninspectedDeeperChildren = checked(
                budget.UninspectedDeeperChildren + regular.Length);
            budget.UninspectedExecutables = checked(
                budget.UninspectedExecutables + executables.Length);
            children = [];
        }
        else
        {
            // Keep all existing ZIP/CAB reservations fail-closed. Only
            // optional EXE probes may be skipped under the global budget,
            // and every skip must be reported as partial coverage.
            if (regular.Length > MaximumInnerArchives -
                budget.ReservedChildren)
                throw new InvalidDataException(
                    "Troppi ZIP/CAB annidati nell'intero pacchetto OEM.");

            var slots = Math.Max(0, Math.Min(
                MaximumNestedExecutableProbes - budget.ExecutableProbes,
                MaximumInnerArchives - budget.ReservedChildren -
                    regular.Length));
            var probes = executables
                .Where(item => item.DeclaredBytes is >= 128 and
                    <= OemArchiveInspector.MaxEntryBytes)
                .Take(slots).ToArray();
            budget.UninspectedExecutables = checked(
                budget.UninspectedExecutables + executables.Length -
                    probes.Length);
            budget.Reserve([.. regular, .. probes]);
            budget.ExecutableProbes += probes.Length;
            children = regular.Concat(probes)
                .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

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
                    using var inputFile = new FileStream(
                        staged, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 64 * 1024,
                        FileOptions.SequentialScan);
                    if (extension == ".exe")
                    {
                        // OemPeZipOverlay verifies PE headers/section bounds
                        // and a real safe ZIP AFTER the PE sections. Unknown
                        // proprietary EXEs are not decoded or executed.
                        Stream overlay;
                        try
                        {
                            overlay = OemPeZipOverlay.Open(inputFile);
                        }
                        catch (NotSupportedException)
                        {
                            budget.UninspectedExecutables++;
                            continue;
                        }

                        using (overlay)
                        {
                            await InspectZipTreeAsync(
                                overlay, devices, architecture,
                                osMajor, osMinor, osBuild, depth + 1,
                                folder, budget, reports, cancellationToken);
                            budget.AcceptedPeZip++;
                        }
                    }
                    else
                    {
                        await InspectZipTreeAsync(
                            inputFile, devices, architecture,
                            osMajor, osMinor, osBuild, depth + 1, folder,
                            budget, reports, cancellationToken);
                    }
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

    private static bool IsExecutable(string path) =>
        Path.GetExtension(path).Equals(".exe",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsUnsupportedPackage(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".msi", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".msix", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".rar", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".iso", StringComparison.OrdinalIgnoreCase);
    }
}
