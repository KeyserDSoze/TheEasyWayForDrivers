namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// A kernel-mode SignTool /kp /c success verifies the selected catalog and
/// membership/signature policy for ONE INF file, not the entire driver package.
/// </summary>
public static class CatalogMemberVerification
{
    public enum Verdict
    {
        VerifiedInfMember,
        Rejected,
        SdkToolUnavailable,
        NotPerformed
    }

    public sealed record Report(Verdict Result)
    {
        public bool IsInfMemberVerified => Result == Verdict.VerifiedInfMember;

        public string Explanation => Result switch
        {
            Verdict.VerifiedInfMember =>
                "Microsoft SignTool ha verificato la firma e l'appartenenza di QUESTO INF al CAT secondo la policy driver Windows. " +
                "I file SYS/DLL e le dipendenze NON sono stati verificati: pacchetto non autorizzato all'installazione.",
            Verdict.Rejected =>
                "Microsoft SignTool ha rifiutato la verifica INF/CAT (firma, appartenenza o policy). " +
                "Non utilizzare il pacchetto.",
            Verdict.SdkToolUnavailable =>
                "SignTool del Windows SDK non presente: appartenenza dell'INF al CAT NON verificata. " +
                "OmegaDrive non installa automaticamente il Windows SDK.",
            _ =>
                "Verifica INF nel CAT non eseguita: nessuna attestazione del pacchetto."
        };
    }

    public static Report FromSignToolExitCode(int exitCode) =>
        new(exitCode == 0 ? Verdict.VerifiedInfMember : Verdict.Rejected);

    public static Report MissingSdk() => new(Verdict.SdkToolUnavailable);
    public static Report NotChecked() => new(Verdict.NotPerformed);
}

/// <summary>
/// Only searches versioned locations inside the official Windows Kits SDK
/// directory, not PATH, working directory, repository, or user-supplied dirs.
/// </summary>
public static class WindowsSdkSignToolLocator
{
    public static IReadOnlyList<string> CandidatePaths(
        string programFilesX86, string architecture, IEnumerable<string> versions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programFilesX86);
        ArgumentNullException.ThrowIfNull(versions);

        var arch = architecture.Trim().ToUpperInvariant() switch
        {
            "X64" or "AMD64" => "x64",
            "ARM64" => "arm64",
            "X86" => "x86",
            _ => null
        };
        if (arch is null)
            return [];

        var root = Path.Combine(programFilesX86, "Windows Kits", "10", "bin");
        return versions
            .Select(v => new { Raw = v, Parsed = Version.TryParse(v, out var parsed) ? parsed : null })
            .Where(v => v.Parsed is not null &&
                        v.Raw.Length is >= 6 and <= 25 &&
                        v.Raw.All(c => char.IsAsciiDigit(c) || c == '.'))
            .OrderByDescending(v => v.Parsed)
            .Select(v => Path.Combine(root, v.Raw, arch, "signtool.exe"))
            .ToArray();
    }
}
