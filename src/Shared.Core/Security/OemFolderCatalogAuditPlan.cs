using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Prepares a bounded read-only set of INF/CAT checks for an OEM folder.
/// The caller may separately ask Windows to check the CAT signature and the
/// membership of each selected INF. This does NOT stage, rank or install a
/// driver, and never proves the entire driver package is trusted.
/// </summary>
public static class OemFolderCatalogAuditPlan
{
    public const int MaxChecks = 12;
    private const int MaxDirectories = LocalOemFolderComparison.MaxDirectories;
    private const int MaxDepth = LocalOemFolderComparison.MaxDepth;
    private const long MaxCatalogBytes = 32L * 1024 * 1024;
    // The folder comparator supports four independent 96-INF batches.
    // Audit the SAME bounded folder scope, without increasing the separate
    // Windows signature and payload execution budgets.
    private const int MaxFolderInf = LocalOemFolderComparison.MaxFolderInfFiles;
    private const long MaxTotalInfBytes =
        (long)OemZipInfEvidence.MaxTotalInfBytes *
        LocalOemFolderComparison.MaxInfBatches;

    private sealed record Candidate(Check Pair, IReadOnlyList<int> DeviceOrdinals);

    public sealed record Check(string InfPath, string CatalogPath)
    {
        // Paths are for passing to Windows verification APIs only.
        // They must never appear in UI status, logs or the copied report.
    }

    public sealed record Plan(
        int InfExamined,
        int MatchedInf,
        int MissingCatalog,
        int UnreadableInf,
        int DeferredInf,
        IReadOnlyList<Check> Checks)
    {
        public bool IsFullPackageVerified => false;
        public bool IsComplete => UnreadableInf == 0 && DeferredInf == 0
            && MissingCatalog == 0;
    }

    public static Plan Create(string rootPath, IReadOnlyList<DriverInfo> devices,
        string architecture, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(devices);
        if (devices.Count > 2048)
            throw new InvalidDataException("Inventario Windows oltre il limite.");

        cancellationToken.ThrowIfCancellationRequested();
        var rootPathFull = Path.GetFullPath(rootPath);
        if (rootPathFull.StartsWith(@"\\", StringComparison.Ordinal) ||
            rootPathFull.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException(
                "Utilizzare una cartella locale per la verifica OEM.");

        var root = new DirectoryInfo(rootPathFull);
        if (!root.Exists)
            throw new InvalidDataException("Cartella OEM non disponibile.");
        for (var parent = root; parent is not null; parent = parent.Parent)
        {
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "La cartella OEM contiene un collegamento non consentito.");
        }

        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((root.FullName, 0));
        int queued = 1, scannedFolders = 0, infExamined = 0;
        int matched = 0, missingCat = 0, invalidInf = 0, deferred = 0;
        long infBytes = 0;
        var candidates = new List<Candidate>();

        // Use only ephemeral inventory ordinals to spread the 12 expensive
        // signature checks across distinct PnP nodes. Never put instance
        // IDs into a Check, report, diagnostic or persistent cache.
        var ordinalsById = new Dictionary<string, HashSet<int>>(
            StringComparer.OrdinalIgnoreCase);
        for (var deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
        {
            foreach (var raw in devices[deviceIndex].HardwareIds.Concat(
                devices[deviceIndex].CompatibleIds))
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                var id = raw.Trim();
                if (id.Length is < 5 or > 160)
                    continue;
                if (!ordinalsById.TryGetValue(id, out var ordinals))
                    ordinalsById.Add(id, ordinals = []);
                ordinals.Add(deviceIndex);
            }
        }

        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (path, depth) = pending.Pop();
            if (depth > MaxDepth || ++scannedFolders > MaxDirectories)
                throw new InvalidDataException(
                    "Troppe directory per una verifica OEM sicura.");

            foreach (var filePath in Directory.EnumerateFiles(
                path, "*.inf", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Path.GetExtension(filePath).Equals(
                    ".inf", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (++infExamined > MaxFolderInf)
                    throw new InvalidDataException(
                        "Più di 384 INF nella cartella OEM.");
                var file = new FileInfo(filePath);
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    file.Length is < 1 or > OemZipInfEvidence.MaxInfBytes)
                {
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException(
                            "INF collegato a un altro percorso: verifica bloccata.");
                    invalidInf++;
                    continue;
                }
                infBytes = checked(infBytes + file.Length);
                if (infBytes > MaxTotalInfBytes)
                    throw new InvalidDataException(
                        "Gli INF superano il limite cartella di 96 MiB.");

                string text;
                try
                {
                    using var stream = new FileStream(
                        filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        8192, FileOptions.SequentialScan);
                    using var sink = new MemoryStream();
                    var buffer = new byte[8192];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (sink.Length + read > OemZipInfEvidence.MaxInfBytes ||
                            sink.Length + read > file.Length)
                            throw new InvalidDataException(
                                "INF OEM modificato durante la lettura.");
                        sink.Write(buffer, 0, read);
                    }
                    if (sink.Length != file.Length)
                        throw new InvalidDataException(
                            "INF OEM modificato durante la lettura.");
                    text = OemInfTextDecoder.Decode(sink.ToArray()).Text;
                }
                catch (InvalidDataException)
                {
                    invalidInf++;
                    continue;
                }

                // The 500-ID reader is for UI preview only. A legitimate
                // NVIDIA/Killer/Realtek model can appear after the 500th
                // unrelated model in a large OEM INF. Match the WHOLE
                // bounded INF to hardware IDs actually inventoried on the
                // current PC, with a separate 4096-target-match ceiling.
                // Invalid Models or malformed Version directives invalidate
                // THIS INF, not all other independently checked packages.
                IReadOnlyList<InfModelEvidence.DeclaredId> matchingIds;
                string? catalogName;
                try
                {
                    matchingIds = InfModelEvidence.ReadMatchingIds(text, devices);
                    if (matchingIds.Count == 0)
                        continue;

                    catalogName = InfPlatformPreflight.GetCatalogFileName(
                        text, architecture);
                }
                catch (InvalidDataException)
                {
                    invalidInf++;
                    continue;
                }

                matched++;
                if (catalogName is null)
                {
                    missingCat++;
                    continue;
                }

                var catalogPath = Path.Combine(path, catalogName);
                var cat = new FileInfo(catalogPath);
                if (!cat.Exists || cat.Length is < 1 or > MaxCatalogBytes)
                {
                    missingCat++;
                    continue;
                }
                if ((cat.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        "Catalogo CAT collegato: verifica bloccata.");

                var relatedOrdinals = matchingIds
                    .SelectMany(id => ordinalsById.TryGetValue(
                        id.HardwareId, out var ordinals)
                        ? ordinals : Enumerable.Empty<int>())
                    .Distinct()
                    .ToArray();
                // ReadMatchingIds only returns IDs from the inventory, so
                // no pair without a real matching device should reach here.
                if (relatedOrdinals.Length == 0)
                    throw new InvalidDataException(
                        "Correlazione PnP OEM non coerente.");
                candidates.Add(new Candidate(
                    new Check(file.FullName, cat.FullName), relatedOrdinals));
            }

            foreach (var child in Directory.EnumerateDirectories(
                path, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        "Junction/symlink non consentito nella cartella OEM.");
                if (depth >= MaxDepth || ++queued > MaxDirectories)
                    throw new InvalidDataException(
                        "Limite directory OEM superato.");
                pending.Push((child, depth + 1));
            }
        }

        var checks = SelectChecks(candidates);
        deferred = candidates.Count - checks.Count;
        return new Plan(infExamined, matched, missingCat, invalidInf,
            deferred, checks);
    }

    /// <summary>
    /// Prefer a CAT/INF pair that can verify the largest number of not-yet
    /// covered distinct devices. Once no new device can be covered, fill
    /// remaining slots with previously deferred INF pairs in encounter
    /// order. This is ONLY diagnostic allocation, not SetupAPI ranking.
    /// </summary>
    private static IReadOnlyList<Check> SelectChecks(
        IReadOnlyList<Candidate> candidates)
    {
        var selected = new List<Check>(Math.Min(MaxChecks, candidates.Count));
        var remaining = Enumerable.Range(0, candidates.Count).ToList();
        var covered = new HashSet<int>();

        while (selected.Count < MaxChecks && remaining.Count > 0)
        {
            var winner = remaining[0];
            var bestGain = -1;
            foreach (var candidateIndex in remaining)
            {
                var newDevices = candidates[candidateIndex].DeviceOrdinals
                    .Count(index => !covered.Contains(index));
                if (newDevices > bestGain)
                {
                    bestGain = newDevices;
                    winner = candidateIndex;
                }
            }

            selected.Add(candidates[winner].Pair);
            covered.UnionWith(candidates[winner].DeviceOrdinals);
            remaining.Remove(winner);
        }

        return selected;
    }
}

/// <summary>
/// Aggregate evidence for opt-in CAT signature and INF member checks.
/// A successful INF membership check is not a full driver-package audit.
/// </summary>
public static class OemFolderCatalogAuditSummary
{
    public sealed record Row(
        CatalogTrustReport.Status CatalogSignature,
        CatalogMemberVerification.Verdict InfMembership);

    public sealed record Report(
        int InfExamined,
        int MatchingInf,
        int MissingCatalog,
        int UnreadableInf,
        int DeferredInf,
        int SignedCatalogs,
        int VerifiedInfMembers,
        int Rejected,
        int NotVerified)
    {
        public bool IsFullPackageVerified => false;
        public bool IsInstallable => false;
        public OemReferencedPayloadAudit.Summary? PayloadAudit { get; init; }
        public string Text =>
            $"{InfExamined} INF esaminati, {MatchingInf} con corrispondenze hardware. " +
            $"{MissingCatalog} senza CAT disponibile, {UnreadableInf} illeggibili, " +
            $"{DeferredInf} oltre il limite del controllo. " +
            $"{SignedCatalogs} CAT con firma Windows valida; " +
            $"{VerifiedInfMembers} INF verificati individualmente nel CAT; " +
            $"{Rejected} verifiche respinte, {NotVerified} non verificate. " +
            "L'esito NON attesta tutti i file SYS/DLL, la completezza, " +
            "il ranking SetupAPI o l'installabilità. " +
            (PayloadAudit is null
                ? "Controllo dei file SYS/DLL non effettuato."
                : PayloadAudit.Text);
    }

    public static Report Create(OemFolderCatalogAuditPlan.Plan plan,
        IEnumerable<Row> checkedRows)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(checkedRows);
        var values = checkedRows.ToArray();
        if (values.Length != plan.Checks.Count)
            throw new InvalidDataException("Numero di verifiche firma inatteso.");

        // Only count a member when both the explicitly paired catalog
        // signature and that individual INF membership have passed.
        var accepted = values.Count(x =>
            x.CatalogSignature == CatalogTrustReport.Status.ValidCatalogSignature &&
            x.InfMembership == CatalogMemberVerification.Verdict.VerifiedInfMember);
        var rejected = values.Count(x =>
            x.CatalogSignature == CatalogTrustReport.Status.InvalidSignature ||
            x.InfMembership == CatalogMemberVerification.Verdict.Rejected);
        return new Report(
            plan.InfExamined, plan.MatchedInf,
            plan.MissingCatalog, plan.UnreadableInf, plan.DeferredInf,
            values.Count(x =>
                x.CatalogSignature == CatalogTrustReport.Status.ValidCatalogSignature),
            accepted, rejected, values.Length - accepted - rejected);
    }
}
