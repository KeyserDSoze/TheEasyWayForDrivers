namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Conservative inventory of files that MAY be driver package payloads.
/// Not an INF CopyFiles/Include/Needs resolver and never proof of completeness.
/// </summary>
public static class DriverPayloadAuditPolicy
{
    public const int MaxInventoryEntries = 256;
    public const int MaxCandidates = 24;
    public const long MaxIndividualBytes = 64L * 1024 * 1024;
    public const long MaxTotalBytes = 256L * 1024 * 1024;

    private static readonly HashSet<string> IncludedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".inf", ".sys", ".dll", ".exe", ".bin", ".dat" };

    public static bool IsCandidateExtension(string? extension) =>
        !string.IsNullOrWhiteSpace(extension) &&
        IncludedExtensions.Contains(extension);

    public sealed record LocalEntry(string RelativePath, long SizeBytes, bool IsReparsePoint);
    public sealed record Plan(IReadOnlyList<LocalEntry> Candidates, int OtherFiles)
    {
        // Even a perfect result for the selected candidates cannot establish
        // full package completeness, signing, OS applicability or installability.
        public bool IsFullPackageVerified => false;
    }

    public enum Outcome
    {
        VerifiedMember,
        Rejected,
        NotVerified
    }

    public sealed record Finding(string RelativePath, string Sha256, Outcome Result);
    public sealed record Summary(int Verified, int Rejected, int NotVerified,
        int OtherFiles)
    {
        public bool IsPackageInstallable => false;
        public string Explanation =>
            $"Audit parziale: {Verified} file nel CAT verificati, {Rejected} respinti, " +
            $"{NotVerified} non verificati, {OtherFiles} file di altro tipo esclusi. " +
            "NON valida la completezza del pacchetto, le direttive INF, i payload non presenti, " +
            "la compatibilità con Windows o l'installazione.";
    }

    public static Plan CreatePlan(IEnumerable<LocalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var candidates = new List<LocalEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var other = 0;
        var scanned = 0;
        long sum = 0;
        foreach (var item in entries)
        {
            if (item is null || ++scanned > MaxInventoryEntries)
                throw new InvalidDataException("Inventario driver mancante o oltre il limite.");
            if (item.IsReparsePoint)
                throw new InvalidDataException("Un file/link reparse impedisce una verifica attendibile.");
            var path = item.RelativePath;
            if (string.IsNullOrWhiteSpace(path) || path.Length > 260 ||
                path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':') ||
                path.Any(char.IsControl) ||
                path.Split('/', '\\').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException("Percorso relativo non sicuro nell'inventario.");

            if (!seen.Add(path.Replace('\\', '/')))
                throw new InvalidDataException("Percorso duplicato nell'inventario del pacchetto.");

            var extension = Path.GetExtension(path);
            if (!IncludedExtensions.Contains(extension))
            {
                other++;
                continue;
            }

            if (item.SizeBytes < 0 || item.SizeBytes > MaxIndividualBytes)
                throw new InvalidDataException("Un file candidato supera il limite previsto.");
            if (candidates.Count >= MaxCandidates ||
                sum > MaxTotalBytes - item.SizeBytes)
                throw new InvalidDataException("Troppi file o dimensione aggregata eccessiva.");
            sum += item.SizeBytes;
            candidates.Add(item);
        }
        return new Plan(candidates, other);
    }

    public static Summary Summarize(IEnumerable<Finding> findings, int otherFiles)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (otherFiles < 0) throw new ArgumentOutOfRangeException(nameof(otherFiles));
        var values = findings.ToArray();
        return new Summary(values.Count(x => x.Result == Outcome.VerifiedMember),
            values.Count(x => x.Result == Outcome.Rejected),
            values.Count(x => x.Result == Outcome.NotVerified), otherFiles);
    }
}
