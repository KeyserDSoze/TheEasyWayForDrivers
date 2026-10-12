using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Conservative, read-only preliminary INF preflight. Windows SetupAPI and
/// signature verification remain authoritative; do not use this to install.
/// </summary>
public static class InfPlatformPreflight
{
    public enum Verdict
    {
        NoHardwareMatch,
        ArchitectureMismatch,
        MinimumWindowsNotMet,
        ManualReviewRequired,
        ModelSectionPotentialMatch
    }

    public sealed record Report(
        Verdict Result,
        int HardwareMatchStrength,
        string Explanation,
        string CatalogName,
        string CatalogStatus,
        string DriverVersion);

    private enum SectionVerdict { Matches, WrongArchitecture, TooNewForWindows, Unknown }

    public static Report Assess(string inf, DriverInfo? device, string architecture,
        int osMajor, int osMinor, int osBuild, bool catalogFileExists)
    {
        if (inf is null) throw new ArgumentNullException(nameof(inf));
        if (inf.Length > 1024 * 1024)
            throw new InvalidDataException("INF oltre il limite di 1 MiB.");

        var catalog = GetCatalogFileName(inf, architecture);
        var catalogStatus = catalog is null
            ? "CatalogFile non dichiarato o non interpretabile; firma non verificata."
            : catalogFileExists
                ? "File CAT presente nella cartella; firma digitale NON verificata."
                : "CatalogFile dichiarato ma CAT assente dalla cartella; firma NON verificata.";
        var driverVer = GetVersionField(inf, "DriverVer") ?? "Non dichiarata";
        Report Build(Verdict verdict, int strength, string explanation) =>
            new(verdict, strength, explanation, catalog ?? "Non determinato",
                catalogStatus, driverVer);

        var signature = GetVersionField(inf, "Signature")?.Trim().Trim('"');
        if (!string.Equals(signature, "$Windows NT$", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(signature, "$Chicago$", StringComparison.OrdinalIgnoreCase))
            return Build(Verdict.ManualReviewRequired, 0,
                "Sezione [Version]/Signature INF assente o non riconosciuta: analisi non conclusiva.");

        // The legacy ID reader collects Models.<suffix> sections for each
        // manufacturer root. Restrict preflight to decorations ACTUALLY
        // declared in [Manufacturer], not similarly named stray sections.
        var allowed = ReadDeclaredModelSections(inf);
        var declared = InfModelEvidence.ReadDeclaredIds(inf)
            .Where(item => allowed.Contains(item.Section))
            .ToArray();
        if (device is null)
            return Build(Verdict.ManualReviewRequired, 0,
                $"INF: {declared.Length} identificativi PnP nelle sezioni Models. Seleziona un dispositivo.");

        var matches = declared
            .Where(x => device.HardwareIds.Any(id =>
                        string.Equals(id?.Trim(), x.HardwareId, StringComparison.OrdinalIgnoreCase)) ||
                        device.CompatibleIds.Any(id =>
                        string.Equals(id?.Trim(), x.HardwareId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (matches.Length == 0)
            return Build(Verdict.NoHardwareMatch, 0,
                $"INF: nessun Hardware ID/Compatible ID corrisponde al dispositivo ({declared.Length} identificativi).");

        int strength = matches.Any(x => device.HardwareIds.Any(id =>
            string.Equals(id?.Trim(), x.HardwareId, StringComparison.OrdinalIgnoreCase))) ? 2 : 1;
        var evidence = strength == 2 ? "Hardware ID esatto" : "Compatible ID (associazione generica)";

        var sections = matches.Select(x => x.Section)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(section => CheckSection(section, architecture, osMajor, osMinor, osBuild))
            .ToArray();

        if (sections.Contains(SectionVerdict.Matches))
            return Build(Verdict.ModelSectionPotentialMatch, strength,
                $"{evidence}; decorazione INF coerente con architettura e versione Windows fornite. " +
                "NON prova compatibilità effettiva, firma, dipendenze o installabilità.");
        if (sections.Contains(SectionVerdict.Unknown))
            return Build(Verdict.ManualReviewRequired, strength,
                $"{evidence}, ma sezione Models non decorata o vincoli TargetOSVersion non interpretati. Verifica tramite Windows e OEM.");
        if (sections.All(x => x == SectionVerdict.WrongArchitecture))
            return Build(Verdict.ArchitectureMismatch, strength,
                $"{evidence} presente solo in sezioni Models di architettura diversa.");
        if (sections.All(x => x is SectionVerdict.TooNewForWindows or SectionVerdict.WrongArchitecture) &&
            sections.Contains(SectionVerdict.TooNewForWindows))
            return Build(Verdict.MinimumWindowsNotMet, strength,
                $"{evidence} dichiarato per una versione/build Windows più recente oppure altra architettura.");
        return Build(Verdict.ManualReviewRequired, strength,
            $"{evidence}; applicabilità della sezione INF non determinabile con certezza.");
    }

    public static string? GetCatalogFileName(string inf, string architecture)
    {
        var arch = NormalizeArchitecture(architecture);
        // Architecture-specific directive takes precedence. We never turn an
        // arbitrary path or UNC name into a file to inspect.
        var keys = arch is null
            ? new[] { "CatalogFile.NT", "CatalogFile" }
            : new[] { "CatalogFile.NT" + arch, "CatalogFile.NT", "CatalogFile" };

        foreach (var key in keys)
        {
            var value = GetVersionField(inf, key);
            if (value is null) continue;
            value = value.Trim().Trim('"');
            if (value.Length is < 5 or > 120 ||
                !value.EndsWith(".cat", StringComparison.OrdinalIgnoreCase) ||
                value is "." or ".." ||
                value.Contains("..", StringComparison.Ordinal) ||
                value.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '-')))
                return null;
            return value;
        }
        return null;
    }

    private static HashSet<string> ReadDeclaredModelSections(string inf)
    {
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool manufacturer = false;
        foreach (var raw in inf.Split('\n'))
        {
            var line = raw.Split(';', 2)[0].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                manufacturer = line.Equals("[Manufacturer]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!manufacturer) continue;
            int equal = line.IndexOf('=');
            if (equal <= 0) continue;
            var items = line[(equal + 1)..].Split(',')
                .Select(item => item.Trim().Trim('"')).ToArray();
            if (items.Length == 0 || items[0].Length == 0 ||
                !items[0].All(IsSafeSectionChar))
                continue;
            if (items.Length == 1)
            {
                sections.Add(items[0]);
                continue;
            }
            foreach (var suffix in items.Skip(1))
            {
                if (suffix.Length == 0 || !suffix.All(IsSafeSectionChar)) continue;
                sections.Add(items[0] + "." + suffix);
            }
        }
        return sections;
    }

    private static bool IsSafeSectionChar(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
            or '_' or '-' or '.';

    private static string? GetVersionField(string inf, string key)
    {
        if (inf.Length > 1024 * 1024) throw new InvalidDataException("INF oltre 1 MiB.");
        bool inVersion = false;
        foreach (var raw in inf.Split('\n'))
        {
            var line = raw.Split(';', 2)[0].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inVersion = line.Equals("[Version]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inVersion) continue;
            int equal = line.IndexOf('=');
            if (equal > 0 && line[..equal].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return line[(equal + 1)..].Trim();
        }
        return null;
    }

    private static SectionVerdict CheckSection(string section, string architecture,
        int major, int minor, int build)
    {
        var parts = section.Split('.');
        // Undecorated Models name: no architecture/OS evidence.
        if (parts.Length == 1) return SectionVerdict.Unknown;
        var decorator = parts[1];
        if (!decorator.StartsWith("NT", StringComparison.OrdinalIgnoreCase))
            return SectionVerdict.Unknown;

        var actual = NormalizeArchitecture(architecture);
        var target = decorator[2..];
        if (actual is null || target.Length == 0) return SectionVerdict.Unknown;
        if (!string.Equals(target, actual, StringComparison.OrdinalIgnoreCase))
        {
            if (target.Equals("amd64", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("arm64", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("x86", StringComparison.OrdinalIgnoreCase))
                return SectionVerdict.WrongArchitecture;
            return SectionVerdict.Unknown;
        }

        if (parts.Length == 2) return SectionVerdict.Matches;
        if (major < 0 || minor < 0 || build < 0) return SectionVerdict.Unknown;
        if (parts.Length < 4 ||
            !int.TryParse(parts[2], out var targetMajor) ||
            !int.TryParse(parts[3], out var targetMinor))
            return SectionVerdict.Unknown;

        // ProductType/SuiteMask decorations have their own semantics and
        // cannot safely be assumed applicable to the current Windows edition.
        if (parts.Length > 4 &&
            (parts.Length != 7 || parts[4].Length > 0 || parts[5].Length > 0))
            return SectionVerdict.Unknown;

        if (major < targetMajor || major == targetMajor && minor < targetMinor)
            return SectionVerdict.TooNewForWindows;
        if (major > targetMajor || major == targetMajor && minor > targetMinor)
            return SectionVerdict.Matches;

        if (parts.Length == 7)
        {
            if (!int.TryParse(parts[6], out var targetBuild))
                return SectionVerdict.Unknown;
            return build < targetBuild
                ? SectionVerdict.TooNewForWindows : SectionVerdict.Matches;
        }
        return SectionVerdict.Matches;
    }

    private static string? NormalizeArchitecture(string? arch) =>
        arch?.Trim().ToUpperInvariant() switch
        {
            "X64" or "AMD64" => "amd64",
            "ARM64" => "arm64",
            "X86" => "x86",
            _ => null
        };
}
