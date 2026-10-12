using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Explains a Windows Update candidate without certifying compatibility or
/// automatically changing installation selection.
/// </summary>
public static class DriverCandidateAssessment
{
    public enum VersionRelation
    {
        Newer,
        Same,
        Older,
        NotComparable
    }

    public sealed record Assessment(
        int HardwareEvidence,
        VersionRelation Relation,
        string Summary);

    public static Assessment Evaluate(DriverInfo installed, DriverUpdateInfo candidate)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(candidate);

        var evidence = DriverUpdateMatcher.MatchStrength(installed, candidate);
        if (evidence == 0)
            return new Assessment(0, VersionRelation.NotComparable,
                "Nessun ID hardware o compatibile corrispondente: non attribuire questo aggiornamento al dispositivo.");

        var prefix = evidence == 2
            ? "Hardware ID esatto."
            : "Compatible ID: corrispondenza più generica, compatibilità da verificare.";

        var provider = candidate.Provider ?? candidate.Manufacturer;
        if (string.IsNullOrWhiteSpace(installed.DriverProvider) ||
            string.IsNullOrWhiteSpace(provider) ||
            !string.Equals(installed.DriverProvider.Trim(), provider.Trim(),
                StringComparison.OrdinalIgnoreCase))
            return new Assessment(evidence, VersionRelation.NotComparable,
                prefix + " Provider installato e candidato differenti o sconosciuti: versione non confrontabile.");

        if (!TryNumericVersion(installed.DriverVersion, out var current) ||
            !TryNumericVersion(candidate.Version, out var proposed))
            return new Assessment(evidence, VersionRelation.NotComparable,
                prefix + " Versione assente o non numerica: confronto non affidabile.");

        var relation = proposed.CompareTo(current) switch
        {
            > 0 => VersionRelation.Newer,
            < 0 => VersionRelation.Older,
            _ => VersionRelation.Same
        };

        var detail = relation switch
        {
            VersionRelation.Newer => "Versione numerica proposta maggiore; non garantisce un aggiornamento appropriato.",
            VersionRelation.Older => "Versione numerica proposta minore: possibile downgrade. Verificare sul sito OEM.",
            VersionRelation.Same => "Versione numerica uguale a quella installata: aggiornamento potenzialmente non necessario.",
            _ => "Versione non confrontabile."
        };

        return new Assessment(evidence, relation, prefix + " " + detail);
    }

    private static bool TryNumericVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 60)
            return false;

        // Version.TryParse alone accepts a single component and uneven formats.
        // Be conservative: accept standard 2–4 unsigned decimal components only.
        var parts = text.Trim().Split('.');
        if (parts.Length is < 2 or > 4 ||
            parts.Any(part => part.Length is < 1 or > 10 ||
                !part.All(c => c is >= '0' and <= '9')))
            return false;

        return Version.TryParse(text.Trim(), out version!);
    }
}
