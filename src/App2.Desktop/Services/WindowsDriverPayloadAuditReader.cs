using System.IO;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Opt-in, read-only audit of bounded *visible* driver file candidates.
/// Does NOT resolve INF CopyFiles/Include/Needs and cannot certify completeness.
/// </summary>
public static class WindowsDriverPayloadAuditReader
{
    public sealed record Report(DriverPayloadAuditPolicy.Summary Summary,
        IReadOnlyList<DriverPayloadAuditPolicy.Finding> Files);

    public static async Task<Report> AuditAsync(
        string infPath, string catalogPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(infPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        var inf = new FileInfo(Path.GetFullPath(infPath));
        var cat = new FileInfo(Path.GetFullPath(catalogPath));
        if (!inf.Exists || !cat.Exists)
            throw new FileNotFoundException("INF o CAT non disponibili.");
        if (inf.DirectoryName is null ||
            !string.Equals(inf.DirectoryName, cat.DirectoryName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Il CAT deve trovarsi accanto all'INF.");

        var root = inf.Directory!;
        if (root.FullName.StartsWith(@"\\", StringComparison.Ordinal) ||
            (root.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Non sono ammesse cartelle UNC o reparse.");

        var entries = new List<DriverPayloadAuditPolicy.LocalEntry>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Sottocartella link/reparse non consentita.");

            foreach (var child in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("File o cartella symlink/reparse non consentiti.");
                if (child is DirectoryInfo subdir)
                {
                    pending.Push(subdir);
                    if (pending.Count > DriverPayloadAuditPolicy.MaxInventoryEntries)
                        throw new InvalidDataException("Troppi elementi nell'albero del pacchetto.");
                    continue;
                }
                if (child is not FileInfo file)
                    throw new InvalidDataException("Tipo di file non previsto nel pacchetto.");
                entries.Add(new DriverPayloadAuditPolicy.LocalEntry(
                    Path.GetRelativePath(root.FullName, file.FullName),
                    file.Length, false));
                if (entries.Count > DriverPayloadAuditPolicy.MaxInventoryEntries)
                    throw new InvalidDataException("Inventario troppo grande per una verifica prudente.");
            }
        }

        var plan = DriverPayloadAuditPolicy.CreatePlan(entries);
        if (!plan.Candidates.Any(x => string.Equals(
                Path.GetFullPath(Path.Combine(root.FullName, x.RelativePath)),
                inf.FullName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("L'INF selezionato non è incluso nell'inventario.");

        var findings = new List<DriverPayloadAuditPolicy.Finding>();
        foreach (var item in plan.Candidates.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(root.FullName, item.RelativePath);
            // The digest is identity information, not an authenticated OEM hash.
            var hash = LocalFileIntegrity.Check(path, DriverPayloadAuditPolicy.MaxIndividualBytes);
            DriverPayloadAuditPolicy.Outcome outcome;
            try
            {
                var result = await WindowsInfCatalogMembershipReader.VerifyPayloadAsync(
                    path, cat.FullName, root.FullName, cancellationToken);
                outcome = result.Result switch
                {
                    CatalogMemberVerification.Verdict.VerifiedInfMember =>
                        DriverPayloadAuditPolicy.Outcome.VerifiedMember,
                    CatalogMemberVerification.Verdict.Rejected =>
                        DriverPayloadAuditPolicy.Outcome.Rejected,
                    _ => DriverPayloadAuditPolicy.Outcome.NotVerified
                };
            }
            catch (Exception error) when (error is IOException or
                InvalidDataException or ArgumentException or InvalidOperationException or
                OperationCanceledException or System.ComponentModel.Win32Exception)
            {
                // Tool error, access failure, timeout, change of file, etc:
                // the file must NOT be reported as verified.
                if (cancellationToken.IsCancellationRequested)
                    throw;
                outcome = DriverPayloadAuditPolicy.Outcome.NotVerified;
            }
            findings.Add(new DriverPayloadAuditPolicy.Finding(
                item.RelativePath, hash.Sha256, outcome));
        }
        return new Report(DriverPayloadAuditPolicy.Summarize(findings, plan.OtherFiles), findings);
    }
}
