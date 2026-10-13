namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Read-only preview of possible source-file dependencies in an INF.
/// This is NOT a SetupAPI interpreter and NEVER establishes package completeness.
/// </summary>
public static class InfDependencyPreview
{
    public const int MaxInfCharacters = 1024 * 1024;
    private const int MaxSections = 160;
    private const int MaxReferences = 80;

    public enum Presence { Present, Missing, Unresolved }

    public sealed record Reference(string SourceName, string? RelativePath, Presence Status);

    public sealed record Report(IReadOnlyList<Reference> References,
        IReadOnlyList<string> Warnings)
    {
        public int MissingCount => References.Count(x => x.Status == Presence.Missing);
        public int UnresolvedCount => References.Count(x => x.Status == Presence.Unresolved);
        public bool IsCompletePackage => false;
        public string Summary =>
            $"Riferimenti INF potenziali: {References.Count}, " +
            $"{MissingCount} file non trovati, {UnresolvedCount} non risolti. " +
            "Analisi parziale: selezione SetupAPI, Include/Needs, altre dipendenze e " +
            "completezza del pacchetto NON verificate.";
    }

    private sealed record Disk(string Folder, bool IsCabinet);
    private sealed record Source(string? RelativePath);

    public static Report Analyze(string inf, IEnumerable<string> inventory, string architecture)
    {
        ArgumentNullException.ThrowIfNull(inf);
        ArgumentNullException.ThrowIfNull(inventory);
        if (inf.Length > MaxInfCharacters)
            throw new InvalidDataException("INF troppo grande per l'analisi delle dipendenze.");

        var sections = ReadSections(inf);
        var warnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Warn(string message)
        {
            if (warnings.Count < 28) warnings.Add(message);
        }

        var arch = architecture.Trim().ToLowerInvariant() switch
        {
            "x64" or "amd64" => "amd64",
            "arm64" => "arm64",
            "x86" => "x86",
            _ => null
        };
        if (arch is null)
            Warn("Architettura Windows non riconosciuta: controllo sezioni limitato.");

        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in inventory)
        {
            if (!SafeRelative(path))
                throw new InvalidDataException("Inventario file con percorso non sicuro.");
            onDisk.Add(Normalize(path));
        }

        var diskMap = new Dictionary<string, Disk>(StringComparer.OrdinalIgnoreCase);
        foreach (var sectionName in new[] { "SourceDisksNames",
            arch is null ? "" : "SourceDisksNames." + arch })
        {
            if (!sections.TryGetValue(sectionName, out var lines)) continue;
            foreach (var line in lines)
            {
                if (!TryPair(line, out var diskId, out var value)) continue;
                var fields = value.Split(',');
                if (!SafeToken(diskId)) { Warn("Disk ID SourceDisksNames non valido."); continue; }
                var folder = fields.Length > 3 ? fields[3].Trim() : "";
                if (!SafeDiskFolder(folder))
                {
                    Warn("Directory SourceDisksNames non interpretabile.");
                    continue;
                }
                var cabinet = fields.Length > 1 &&
                    fields[1].Trim().EndsWith(".cab", StringComparison.OrdinalIgnoreCase);
                diskMap[diskId] = new Disk(Normalize(folder.TrimStart('\\', '/')), cabinet);
            }
        }

        var sources = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);
        foreach (var sectionName in new[] { "SourceDisksFiles",
            arch is null ? "" : "SourceDisksFiles." + arch })
        {
            if (!sections.TryGetValue(sectionName, out var lines)) continue;
            foreach (var line in lines)
            {
                if (!TryPair(line, out var file, out var value)) continue;
                if (!SafeFilename(file)) { Warn("Nome sorgente SourceDisksFiles non valido."); continue; }
                var fields = value.Split(',');
                if (fields.Length == 0 || !diskMap.TryGetValue(fields[0].Trim(), out var disk))
                {
                    sources[file] = new Source(null);
                    Warn("Riferimento SourceDisksFiles senza disco SourceDisksNames risolvibile.");
                    continue;
                }
                var subdir = fields.Length > 1 ? fields[1].Trim() : "";
                if (!SafeDiskFolder(subdir) || disk.IsCabinet)
                {
                    sources[file] = new Source(null);
                    Warn("Sorgente in CAB o con sottocartella non interpretabile.");
                    continue;
                }
                var relative = Join(disk.Folder, subdir.TrimStart('\\', '/'), file);
                sources[file] = new Source(SafeRelative(relative) ? Normalize(relative) : null);
                if (sources[file].RelativePath is null)
                    Warn("Percorso SourceDisksFiles non sicuro.");
            }
        }

        // Installation-section names are read only from Manufacturer -> Models
        // entries. They are NOT selected by PnP/SetupAPI, so we report potential
        // references rather than mandatory dependencies for this machine.
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sections.TryGetValue("Manufacturer", out var manufacturer))
        {
            foreach (var line in manufacturer)
            {
                if (!TryPair(line, out _, out var value)) continue;
                var fields = value.Split(',').Select(x => x.Trim()).ToArray();
                if (!SafeToken(fields[0])) { Warn("Sezione Models non valida."); continue; }
                if (fields.Length == 1)
                    roots.Add(fields[0]);
                else
                    foreach (var decoration in fields.Skip(1))
                    {
                        if (!SafeToken(decoration)) { Warn("Decorazione Models non gestita."); continue; }
                        if (MatchesArch(decoration, arch))
                            roots.Add(fields[0] + "." + decoration);
                    }
            }
        }

        var installs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var models in roots)
        {
            if (!sections.TryGetValue(models, out var entries))
            {
                Warn("Sezione Models dichiarata ma non trovata o non selezionata.");
                continue;
            }
            foreach (var line in entries)
            {
                if (!TryPair(line, out _, out var value)) continue;
                var install = value.Split(',')[0].Trim();
                if (SafeToken(install)) installs.Add(install);
                else Warn("Sezione installazione Models non interpretabile.");
            }
        }
        installs.Add("DefaultInstall");
        installs.Add("ClassInstall32");
        if (roots.Count == 0)
            Warn("Nessuna sezione Models applicabile trovata: installazioni PnP non risolte.");

        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var encounteredCopy = false;
        foreach (var (section, lines) in sections)
        {
            if (!IsInstallSection(section, installs, arch)) continue;
            foreach (var line in lines)
            {
                if (!TryPair(line, out var key, out var value)) continue;
                if (key.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Needs", StringComparison.OrdinalIgnoreCase))
                {
                    Warn("Include/Needs rilevati: sezioni esterne o dipendenze non risolte.");
                    continue;
                }
                if (key.Equals("CopyINF", StringComparison.OrdinalIgnoreCase))
                {
                    Warn("CopyINF rilevato: file INF aggiuntivi non risolti.");
                    continue;
                }
                if (!key.Equals("CopyFiles", StringComparison.OrdinalIgnoreCase)) continue;
                encounteredCopy = true;
                foreach (var field in value.Split(','))
                {
                    var target = field.Trim();
                    if (target.StartsWith('@'))
                    {
                        AddReference(target[1..]);
                        continue;
                    }
                    if (!SafeToken(target) || !sections.TryGetValue(target, out var list))
                    {
                        Warn("Sezione di elenco CopyFiles assente o non interpretabile.");
                        continue;
                    }
                    foreach (var record in list)
                    {
                        // dest-name[,source-name[,unused[,flags]]]
                        // Copy source, not renamed destination.
                        var parts = record.Split(',');
                        var source = parts.Length > 1 && parts[1].Trim().Length > 0
                            ? parts[1].Trim() : parts[0].Trim();
                        AddReference(source);
                    }
                }
            }
        }

        if (!encounteredCopy)
            Warn("Nessun CopyFiles interpretato: non significa che il driver non richieda file.");

        var results = requested.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                if (!sources.TryGetValue(name, out var source) || source.RelativePath is null)
                    return new Reference(name, null, Presence.Unresolved);
                return new Reference(name, source.RelativePath,
                    onDisk.Contains(source.RelativePath) ? Presence.Present : Presence.Missing);
            }).ToArray();
        return new Report(results, warnings.ToArray());

        void AddReference(string file)
        {
            if (!SafeFilename(file))
            {
                Warn("Riferimento CopyFiles con nome non sicuro o variabile INF.");
                return;
            }
            if (!requested.Add(file)) return;
            if (requested.Count > MaxReferences)
                throw new InvalidDataException("Troppe dipendenze INF per analisi prudente.");
        }
    }

    private static bool IsInstallSection(string section,
        HashSet<string> installRoots, string? architecture)
    {
        foreach (var root in installRoots)
        {
            if (section.Equals(root, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!section.StartsWith(root + ".", StringComparison.OrdinalIgnoreCase))
                continue;
            var suffix = section[(root.Length + 1)..];
            if (suffix.Equals("NT", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("CoInstallers", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("Services", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("Interfaces", StringComparison.OrdinalIgnoreCase))
                return true;
            if (suffix.StartsWith("NT", StringComparison.OrdinalIgnoreCase) &&
                MatchesArch(suffix, architecture))
                return true;
        }
        return false;
    }

    private static bool MatchesArch(string value, string? arch)
    {
        if (arch is null) return false;
        var lower = value.ToLowerInvariant();
        if (lower == "nt") return true;
        return lower == "nt" + arch || lower.StartsWith("nt" + arch + ".", StringComparison.Ordinal);
    }

    private static Dictionary<string, List<string>> ReadSections(string inf)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;
        var lineCount = 0;
        foreach (var raw in inf.Split('\n'))
        {
            if (++lineCount > 12000) throw new InvalidDataException("Troppi record INF.");
            var line = raw.Split(';', 2)[0].Trim();
            if (line.Length == 0) continue;
            if (line.Length > 4096) throw new InvalidDataException("Riga INF eccessiva.");
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var key = line[1..^1].Trim();
                if (!SafeToken(key) || sections.ContainsKey(key) || sections.Count >= MaxSections)
                    throw new InvalidDataException("Sezione INF duplicata, non valida o eccessiva.");
                current = [];
                sections.Add(key, current);
                continue;
            }
            current?.Add(line);
        }
        return sections;
    }

    private static bool TryPair(string line, out string key, out string value)
    {
        var at = line.IndexOf('=');
        key = at > 0 ? line[..at].Trim() : "";
        value = at > 0 ? line[(at + 1)..].Trim() : "";
        return at > 0;
    }

    private static bool SafeToken(string text) =>
        text.Length is > 0 and <= 100 && text.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' or '-');

    private static bool SafeFilename(string text) =>
        text.Length is > 0 and <= 120 &&
        text is not "." and not ".." && !text.Contains("..", StringComparison.Ordinal) &&
        !text.Contains('%') &&
        text.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '+' or '(' or ')' or ' ');

    private static bool SafeDiskFolder(string text)
    {
        if (text.Length > 120 || text.Contains(':') || text.Contains('%') || text.Contains("..", StringComparison.Ordinal))
            return false;
        var normalized = text.TrimStart('\\', '/');
        return normalized.Length == 0 || SafeRelative(normalized);
    }

    private static bool SafeRelative(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > 260 || path.StartsWith('/') || path.StartsWith('\\') ||
            path.Contains(':') || path.Any(char.IsControl))
            return false;
        return path.Split('/', '\\').All(part =>
            part.Length is > 0 and <= 120 &&
            part is not "." and not ".." && !part.Contains('%') &&
            part.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '+' or '(' or ')' or ' '));
    }

    private static string Join(params string[] parts) =>
        string.Join("/", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Normalize));
    private static string Normalize(string value) => value.Replace('\\', '/');
}
