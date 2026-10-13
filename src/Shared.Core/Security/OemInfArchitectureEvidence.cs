using TheEasyWayForDrivers.Core.Drivers;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>Architectural hints from declared INF Models names, never SetupAPI applicability.</summary>
public static class OemInfArchitectureEvidence
{
    public enum Verdict { ArchitectureMatches, ArchitectureDiffers, Unknown }
    public sealed record Finding(string InfPath, string Section, Verdict Result, string Explanation);
    public sealed record Report(IReadOnlyList<Finding> Findings)
    {
        public bool IsInstallable => false;
        public bool IsVerifiedCompatible => false;
    }

    public static Report Assess(OemZipInfEvidence.Report evidence, string? architecture)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var arch = architecture?.Trim().ToLowerInvariant() switch
        {
            "x64" or "amd64" => "amd64",
            "arm64" => "arm64",
            "x86" => "x86",
            _ => null
        };
        var findings = new List<Finding>();
        foreach (var file in evidence.InfFiles)
        foreach (var section in file.DeclaredIds.Select(id => id.Section)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Only interpret explicit NT architecture decoration. Every other
            // decoration and OS constraint still requires native Windows review.
            var pieces = section.Split('.');
            var token = pieces.Length >= 2 ? pieces[1].ToLowerInvariant() : "";
            var declared = token switch
            {
                "ntamd64" => "amd64",
                "ntarm64" => "arm64",
                "ntx86" => "x86",
                _ => null
            };
            var result = arch is null || declared is null ? Verdict.Unknown :
                arch == declared ? Verdict.ArchitectureMatches : Verdict.ArchitectureDiffers;
            var explanation = pieces.Length > 2
                ? "Vincoli OS/TargetOSVersion non valutati; nessuna compatibilità certificata."
                : "Solo confronto architettura dichiarata; applicabilità Windows non verificata.";
            findings.Add(new Finding(file.Path, section, result, explanation));
        }
        return new Report(findings);
    }
}
