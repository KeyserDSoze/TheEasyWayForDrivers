using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Read-only, bounded evidence for *possible* CopyFiles payloads of one INF.
/// Windows SetupAPI remains the authority for actual selected models/Needs,
/// driver ranking, package completeness, and staging/install decisions.
/// Paths exist only to run the Windows SDK signature checks, never for logs/UI.
/// </summary>
public static class OemReferencedPayloadAudit
{
    public const int MaxFilesPerInf = 8;
    public const int MaxFilesPerReview = 8;
    // Only this opt-in OEM folder name inventory gets a larger bound.
    // 384 INF plus CAT/SYS/DLL often exceed the old 256 entries;
    // WindowsDriverPayloadAuditReader keeps its independent 256 cap.
    public const int MaxFolderInventoryEntries = 1024;
    private const int MaxDirectoryDepth = 8;
    private const long MaxInfBytes = OemZipInfEvidence.MaxInfBytes;

    public sealed record Plan(
        int References,
        int Present,
        int Missing,
        int Unresolved,
        int WarningCount,
        int Deferred,
        bool InventoryUnscannable,
        IReadOnlyList<string> CandidatePaths)
    {
        public bool IsPackageComplete => false;
        public static Plan NotAnalyzed() =>
            new(0, 0, 0, 0, 0, 0, true, []);
    }

    public sealed record Reviewed(
        Plan Plan,
        IReadOnlyList<DriverPayloadAuditPolicy.Outcome> MembershipResults,
        int AdditionalDeferred);

    public sealed record Summary(
        int ReferencedFiles,
        int PresentFiles,
        int MissingFiles,
        int UnresolvedFiles,
        int InfWithWarnings,
        int UnanalyzedInf,
        int DeferredFileChecks,
        int VerifiedFileMembers,
        int RejectedFileMembers,
        int NotVerifiedFileMembers)
    {
        public bool IsPackageComplete => false;
        public bool IsInstallable => false;

        public string Text =>
            $"Riferimenti ai file OEM (analisi INF parziale): {ReferencedFiles}; " +
            $"{PresentFiles} presenti, {MissingFiles} possibili mancanti, " +
            $"{UnresolvedFiles} non risolti. " +
            $"{VerifiedFileMembers} file verificati singolarmente nel CAT, " +
            $"{RejectedFileMembers} respinti, {NotVerifiedFileMembers} non verificati, " +
            $"{DeferredFileChecks} controlli rinviati. " +
            $"{UnanalyzedInf} INF non analizzati, {InfWithWarnings} INF con avvertenze. " +
            "Questi file NON sono l'elenco completo SetupAPI; ranking Windows " +
            "per pacchetti OEM locali non calcolato. Nessuna installazione autorizzata.";
    }

    /// <summary>
    /// Allocates the fixed eight read-only SignTool payload checks fairly
    /// across already trusted, individually CAT-verified INF pairs.
    /// Callers pass ZERO for untrusted/unanalyzable INFs. The preceding
    /// CAT planner orders INF pairs by new PnP-device coverage, so the first
    /// round prioritizes diverse devices. This is NOT installation ranking.
    /// No path, Device Instance ID or file content is accepted here.
    /// </summary>
    public static IReadOnlyList<int> AllocateChecks(
        IReadOnlyList<int> eligiblePerInf)
    {
        ArgumentNullException.ThrowIfNull(eligiblePerInf);
        if (eligiblePerInf.Count > OemFolderCatalogAuditPlan.MaxChecks ||
            eligiblePerInf.Any(count => count < 0 || count > MaxFilesPerInf))
            throw new InvalidDataException(
                "Numero file o coppie INF oltre il budget OEM.");

        var allocated = new int[eligiblePerInf.Count];
        var remaining = MaxFilesPerReview;
        // Round robin: one file per trusted INF before any second file.
        // Stops even for zero eligible files; no repeated scans of a
        // potentially unbounded or untrusted user-supplied list.
        for (var round = 0; round < MaxFilesPerInf && remaining > 0; round++)
        {
            for (var i = 0; i < eligiblePerInf.Count && remaining > 0; i++)
            {
                if (eligiblePerInf[i] <= round)
                    continue;
                allocated[i]++;
                remaining--;
            }
        }
        return allocated;
    }

    public static Summary Summarize(IEnumerable<Reviewed> reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        var entries = reviewed.ToArray();
        foreach (var item in entries)
        {
            if (item.AdditionalDeferred < 0 ||
                item.MembershipResults.Count > item.Plan.CandidatePaths.Count ||
                item.AdditionalDeferred + item.MembershipResults.Count !=
                    item.Plan.CandidatePaths.Count)
                throw new InvalidDataException("Contabilizzazione verifiche payload incoerente.");
        }

        return new(
            entries.Sum(x => x.Plan.References),
            entries.Sum(x => x.Plan.Present),
            entries.Sum(x => x.Plan.Missing),
            entries.Sum(x => x.Plan.Unresolved),
            entries.Count(x => x.Plan.WarningCount > 0),
            entries.Count(x => x.Plan.InventoryUnscannable),
            entries.Sum(x => x.Plan.Deferred + x.AdditionalDeferred),
            entries.Sum(x => x.MembershipResults.Count(v =>
                v == DriverPayloadAuditPolicy.Outcome.VerifiedMember)),
            entries.Sum(x => x.MembershipResults.Count(v =>
                v == DriverPayloadAuditPolicy.Outcome.Rejected)),
            entries.Sum(x => x.MembershipResults.Count(v =>
                v == DriverPayloadAuditPolicy.Outcome.NotVerified)));
    }

    /// <summary>
    /// Uses the catalog's folder as a conservative package root. Directories
    /// are not followed through reparse points; a large/unusual directory is
    /// "not analyzed", NEVER a positive full-package trust result.
    /// </summary>
    public static Plan Prepare(
        string infPath, string catPath, string architecture,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(infPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(catPath);
        cancellationToken.ThrowIfCancellationRequested();

        var inf = new FileInfo(Path.GetFullPath(infPath));
        var cat = new FileInfo(Path.GetFullPath(catPath));
        if (!inf.Exists || !cat.Exists ||
            !inf.Extension.Equals(".inf", StringComparison.OrdinalIgnoreCase) ||
            !cat.Extension.Equals(".cat", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(inf.DirectoryName, cat.DirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
            inf.Length is < 1 or > MaxInfBytes ||
            (inf.Attributes & FileAttributes.ReparsePoint) != 0 ||
            (cat.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("INF e CAT non sono una coppia locale verificabile.");

        var folder = inf.Directory!;
        for (DirectoryInfo? current = folder; current is not null;
            current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Cartella OEM collegata non consentita.");
        }

        byte[] bytes;
        using (var source = new FileStream(
            inf.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
            8192, FileOptions.SequentialScan))
        using (var sink = new MemoryStream())
        {
            var buffer = new byte[8192];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sink.Length + read > MaxInfBytes ||
                    sink.Length + read > inf.Length)
                    throw new InvalidDataException("INF modificato durante l'analisi.");
                sink.Write(buffer, 0, read);
            }
            if (sink.Length != inf.Length)
                throw new InvalidDataException("INF modificato durante l'analisi.");
            bytes = sink.ToArray();
        }

        var infText = OemInfTextDecoder.Decode(bytes).Text;
        var inventory = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(string Folder, int Depth)>();
        pending.Push((folder.FullName, 0));
        var scanned = 0;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, depth) = pending.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(
                current, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++scanned > MaxFolderInventoryEntries)
                    return Plan.NotAnalyzed();

                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Link nella cartella payload OEM.");

                if ((attrs & FileAttributes.Directory) != 0)
                {
                    if (depth >= MaxDirectoryDepth)
                        return Plan.NotAnalyzed();
                    pending.Push((path, depth + 1));
                    continue;
                }

                var relative = Path.GetRelativePath(folder.FullName, path)
                    .Replace('\\', '/');
                if (relative.Length > 260 || relative.Split('/').Any(
                    part => part is "" or "." or "..") ||
                    relative.Any(char.IsControl))
                    throw new InvalidDataException("Percorso payload OEM non sicuro.");
                if (!inventory.TryAdd(relative, path))
                    throw new InvalidDataException("Percorso payload duplicato.");
            }
        }

        var preview = InfDependencyPreview.Analyze(infText,
            inventory.Keys, architecture);
        var candidates = new List<string>();
        var postponed = 0;
        foreach (var reference in preview.References)
        {
            if (reference.Status != InfDependencyPreview.Presence.Present ||
                reference.RelativePath is null)
                continue;
            if (!inventory.TryGetValue(reference.RelativePath, out var path))
                throw new InvalidDataException("File payload non presente nell'inventario.");

            var file = new FileInfo(path);
            if (!DriverPayloadAuditPolicy.IsCandidateExtension(file.Extension) ||
                file.Length < 1 || file.Length > DriverPayloadAuditPolicy.MaxIndividualBytes)
            {
                postponed++;
                continue;
            }

            if (candidates.Count < MaxFilesPerInf)
                candidates.Add(file.FullName);
            else
                postponed++;
        }

        return new Plan(
            preview.References.Count,
            preview.PresentCount,
            preview.MissingCount,
            preview.UnresolvedCount,
            preview.Warnings.Count,
            postponed,
            false,
            candidates);
    }
}
