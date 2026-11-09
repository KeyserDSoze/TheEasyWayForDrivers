using System.IO;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// One opt-in OEM driver folder -> existing bounded ZIP/CAB/PE-ZIP and
/// extracted-INF readers. Purely local and read-only. Any corrupt supported
/// source fails the entire review; unsupported proprietary installers are
/// counted separately so the result never claims exhaustive coverage.
/// </summary>
public static class PcSpecialistLocalCatalogReader
{
    public sealed record Result(
        LocalOemArchiveBatchSummary.Report Evidence,
        int ZipCabSources,
        int PeZipSources,
        bool ExtractedInfIncluded,
        bool ExtractedInfHasEvidence,
        int UnsupportedPackages,
        int FilesVisited)
    {
        public bool IsInstallable => false;
        public bool IsVerifiedOfficialSource => false;
        public PcSpecialistPackageReconciliation.Report? SnapshotComparison
        {
            get;
            init;
        }
        public bool IsPartial => UnsupportedPackages > 0 ||
            Evidence.IsPartial;

        public string Summary =>
            $"{Evidence.ArchivesExamined} sorgenti locali esaminate " +
            $"({ZipCabSources} ZIP/CAB, {PeZipSources} EXE PE-ZIP, " +
            $"{(ExtractedInfIncluded ? 1 : 0)} raccolta INF estratti); " +
            $"{Evidence.InfExamined} file INF letti, " +
            $"{Evidence.RejectedInfCount} INF scartati; " +
            (Evidence.DistinctMatchedDevices is int unique
                ? $"{unique} dispositivi distinti con evidenze INF, " +
                  $"{Evidence.TotalAssociations} associazioni totali; "
                : $"{Evidence.TotalAssociations} associazioni INF " +
                  "(dispositivi distinti non verificabili); ") +
            $"{Evidence.Findings.Count} righe mostrate, " +
            $"{Evidence.RowsOmitted} oltre il limite; " +
            $"{UnsupportedPackages} file EXE/MSI/7z o formati proprietari " +
            "esterni non analizzati; " +
            $"{Evidence.UninspectedNestedExecutables} EXE interni, " +
            $"{Evidence.UninspectedProprietaryPackages} MSI/7z/RAR/ISO interni, " +
            $"{Evidence.UninspectedDeeperArchives} ZIP/CAB oltre profondità " +
            "non analizzati. " +
            (IsPartial ? "COPERTURA PARZIALE: non dedurre assenza di driver. " : "") +
            "Solo confronto locale; origine del catalogo, firme CAT, " +
            "ranking e installabilità NON verificati. " +
            "Nessun driver installato, nessun numero ordine trasmesso.";
    }

    public static async Task<Result> CompareAsync(
        string folder, IReadOnlyList<DriverInfo> devices,
        string architecture, int osMajor, int osMinor, int osBuild,
        CancellationToken cancellationToken,
        IReadOnlyList<PcSpecialistPortalSnapshot.Package>? savedSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (devices.Count > 2048)
            throw new InvalidDataException("Inventario PnP oltre il limite.");

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        budget.CancelAfter(TimeSpan.FromMinutes(5));

        // No on-the-fly results: even one unsafe ZIP/CAB or path aborts
        // the entire comparison instead of claiming partial success.
        var plan = await Task.Run(() =>
            PcSpecialistLocalCatalogPlan.Create(folder, budget.Token),
            budget.Token);
        var reports = new List<LocalOemDriverComparison.Report>(plan.SourceCount);
        // Index archive outcomes strictly by the local basename, then
        // join to names already validated by the saved HTML parser.
        // No original file paths, PnP IDs or local source names are
        // exposed by the reconciliation.
        var sourceRows = new List<PcSpecialistPackageReconciliation.LocalSource>();
        var extractedInfHasEvidence = false;

        if (plan.HasLooseInf)
        {
            var extracted = await Task.Run(() =>
                LocalOemFolderComparison.Compare(
                    plan.RootPath, devices, architecture,
                    osMajor, osMinor, osBuild, budget.Token), budget.Token);
            // No driver evidence if the directory changed after planning.
            if (extracted.InfExamined == 0)
                throw new InvalidDataException(
                    "Gli INF locali sono cambiati durante la verifica.");
            reports.Add(extracted);
            extractedInfHasEvidence = extracted.TotalMatchedDevices > 0;
        }

        foreach (var archive in plan.Archives)
        {
            budget.Token.ThrowIfCancellationRequested();
            var file = new FileInfo(archive);
            var extension = file.Extension.ToLowerInvariant();
            var maximum = extension == ".zip"
                ? OemArchiveInspector.MaxZipArchiveBytes
                : OemArchiveInspector.MaxArchiveBytes;
            if (!file.Exists || file.Length is < 4 ||
                file.Length > maximum ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "Un pacchetto ZIP/CAB/EXE PE-ZIP è cambiato durante la verifica.");

            if (extension == ".cab")
            {
                reports.Add(await LocalOemCabComparison.CompareAsync(
                    archive, devices, architecture, osMajor, osMinor,
                    osBuild, budget.Token));
            }
            else
            {
                reports.Add(await Task.Run(async () =>
                {
                    using var input = new FileStream(archive, FileMode.Open,
                        FileAccess.Read, FileShare.Read,
                        65536, FileOptions.SequentialScan);
                    return extension == ".exe"
                        ? await LocalOemPeZipComparison.CompareAsync(
                            input, devices, architecture, osMajor, osMinor,
                            osBuild, budget.Token)
                        : await LocalOemNestedArchiveComparison.CompareAsync(
                            input, devices, architecture, osMajor, osMinor,
                            osBuild, budget.Token);
                }, budget.Token));
            }
            sourceRows.Add(PcSpecialistPackageReconciliation.LocalSource
                .FromArchive(Path.GetFileName(archive), reports[^1]));
        }

        budget.Token.ThrowIfCancellationRequested();
        var summary = LocalOemArchiveBatchSummary.Combine(reports);
        PcSpecialistPackageReconciliation.Report? reconciliation = null;
        if (savedSnapshot is { Count: > 0 })
        {
            sourceRows.AddRange(plan.UnsupportedPackageNames.Select(
                name => PcSpecialistPackageReconciliation.LocalSource
                    .Unsupported(name)));
            reconciliation = PcSpecialistPackageReconciliation.Compare(
                savedSnapshot, sourceRows);
        }

        return new Result(summary,
            plan.Archives.Count(path =>
                !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)),
            plan.Archives.Count(path =>
                path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)),
            plan.HasLooseInf, extractedInfHasEvidence,
            plan.UnsupportedPackages, plan.FilesVisited)
        {
            SnapshotComparison = reconciliation
        };
    }
}
