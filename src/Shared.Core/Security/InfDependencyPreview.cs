using System.Text;
using TheEasyWayForDrivers.Core.Drivers;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Read-only preview of possible source-file dependencies in an INF.
/// This is NOT a SetupAPI interpreter and NEVER establishes package completeness.
/// </summary>
public static class InfDependencyPreview
{
    // The OEM ZIP/folder and catalog plan already accept up to 4 MiB
    // of INF text. Keep the dependency preview within that SAME byte
    // budget; separate section/line/reference limits still fail closed.
    public const int MaxInfCharacters = OemZipInfEvidence.MaxInfBytes;
    private const int MaxSections = 160;
    private const int MaxReferences = 80;

    public enum Presence { Present, Missing, Unresolved }

    public sealed record Reference(string SourceName, string? RelativePath, Presence Status, string Origin = "CopyFiles");

    public sealed record Report(IReadOnlyList<Reference> References,
        IReadOnlyList<string> Warnings)
    {
        public int PresentCount => References.Count(x => x.Status == Presence.Present);
        public int MissingCount => References.Count(x => x.Status == Presence.Missing);
        public int UnresolvedCount => References.Count(x => x.Status == Presence.Unresolved);
        public bool IsCompletePackage => false;
        public bool RequiresManualReview => MissingCount > 0 ||
            UnresolvedCount > 0 || Warnings.Count > 0 || !IsCompletePackage;

        /// <summary>Deterministic, read-only evidence grouped by confidence.</summary>
        public IReadOnlyList<string> EvidenceLines()
        {
            var lines = new List<string>();
            foreach (var status in new[] { Presence.Missing, Presence.Unresolved, Presence.Present })
            {
                var matches = References.Where(x => x.Status == status)
                    .OrderBy(x => x.SourceName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Origin, StringComparer.OrdinalIgnoreCase).ToArray();
                var heading = status switch
                {
                    Presence.Missing => "POSSIBILI FILE MANCANTI",
                    Presence.Unresolved => "ORIGINI NON RISOLTE",
                    _ => "FILE PRESENTI (firma e applicabilità non dimostrate)"
                };
                lines.Add(heading + " (" + matches.Length + ")");
                foreach (var reference in matches)
                    lines.Add("• [" + reference.Origin + "] " + reference.SourceName +
                        (reference.RelativePath is null ? "" : " (" + reference.RelativePath + ")"));
            }
            lines.Add("AVVERTENZE (" + Warnings.Count + ")");
            lines.AddRange(Warnings.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(x => "• " + x));
            lines.Add("Nessuna installazione OEM autorizzata: l'analisi non certifica completezza, firma o applicabilità.");
            return lines;
        }
        public string Summary =>
            $"Riferimenti INF potenziali: {References.Count}, " +
            $"{PresentCount} presenti, {MissingCount} possibili file mancanti, {UnresolvedCount} non risolti. " +
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

        // Single-level bounded [Strings] substitution; never expand nested tokens.
        // Duplicate keys (including identical/case-only duplicates) are
        // ambiguous in an untrusted INF. Never trust the first declaration
        // as a local SYS/DLL source for subsequent CAT membership checks.
        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenStringKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sections.TryGetValue("Strings", out var stringLines))
            foreach (var line in stringLines)
            {
                if (!TryPair(line, out var name, out var value) || !SafeToken(name)) continue;
                if (!seenStringKeys.Add(name))
                {
                    // The third occurrence must not restore a previously
                    // invalidated key. Never expose token names or values
                    // in warning text: some vendor INFs include private data.
                    strings.Remove(name);
                    Warn("Token [Strings] ripetuto: espansione non risolta.");
                    continue;
                }
                value = value.Trim().Trim('"');
                if (value.Length is > 0 and <= 120 &&
                    !value.Contains('%') && !value.Any(char.IsControl))
                    strings.TryAdd(name, value);
            }
        string Expand(string value)
        {
            value = value.Trim();
            return value.Length > 2 && value[0] == '%' && value[^1] == '%' &&
                strings.TryGetValue(value[1..^1], out var literal) ? literal : value;
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
            // Generic and architecture-decorated sections may legitimately
            // override each other. Repeated IDs WITHIN one section are
            // ambiguous, however, and must not become positive evidence.
            var seenDisksInSection = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                if (!TryPair(line, out var diskId, out var value)) continue;
                if (!SafeToken(diskId)) { Warn("Disk ID SourceDisksNames non valido."); continue; }
                if (!seenDisksInSection.Add(diskId))
                {
                    // Keep a negative sentinel even on a third duplicate,
                    // regardless of which order the conflicting paths used.
                    diskMap[diskId] = new Disk("", true);
                    Warn("Disk ID SourceDisksNames duplicato: origine non risolta.");
                    continue;
                }
                var fields = SplitInfFields(value);
                if (fields is null)
                {
                    // A malformed decorated disk must never inherit a
                    // potentially unrelated generic disk path as Present.
                    diskMap[diskId] = new Disk("", true);
                    Warn("Campi SourceDisksNames con virgolette non interpretabili.");
                    continue;
                }
                var folder = fields.Length > 3 ? fields[3].Trim() : "";
                if (!SafeDiskFolder(folder))
                {
                    // An invalid architecture-specific declaration must
                    // shadow its generic counterpart instead of silently
                    // reusing an unrelated, apparently present source.
                    diskMap[diskId] = new Disk("", true);
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
            var seenFilesInSection = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                if (!TryPair(line, out var file, out var value)) continue;
                file = Expand(file);
                if (!SafeFilename(file)) { Warn("Nome sorgente SourceDisksFiles non valido."); continue; }
                if (!seenFilesInSection.Add(file))
                {
                    // A later valid-looking entry cannot resolve an
                    // ambiguous duplicate in the same source section.
                    sources[file] = new Source(null);
                    Warn("File SourceDisksFiles duplicato: origine non risolta.");
                    continue;
                }
                var fields = SplitInfFields(value);
                if (fields is null || fields.Length == 0 ||
                    !diskMap.TryGetValue(fields[0].Trim(), out var disk))
                {
                    sources[file] = new Source(null);
                    Warn("Riferimento SourceDisksFiles senza disco o campi risolvibili.");
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
                        {
                            roots.Add(fields[0] + "." + decoration);
                            if (decoration.Split('.').Length > 1)
                                Warn("TargetOSVersion/ProductType/SuiteMask non verificati: sezione solo potenziale.");
                        }
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
                var install = Expand(value.Split(',')[0].Trim());
                if (SafeToken(install)) installs.Add(install);
                else Warn("Sezione installazione Models non interpretabile.");
            }
        }
        installs.Add("DefaultInstall");
        installs.Add("ClassInstall32");
        if (roots.Count == 0)
            Warn("Nessuna sezione Models applicabile trovata: installazioni PnP non risolte.");

        var requested = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var encounteredCopy = false;
        const int maxNeedsDepth = 12;
        const int maxVisitedSections = 80;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Needs without Include may reference a section in this same INF. We
        // follow only those local references; external INF resolution requires
        // Windows SetupAPI context and is deliberately outside this preview.
        void Visit(string section, int depth)
        {
            if (depth > maxNeedsDepth)
            {
                Warn("Limite profondità Needs raggiunto: dipendenze non risolte.");
                return;
            }
            if (active.Contains(section))
            {
                Warn("Ciclo Needs rilevato: dipendenze non risolte.");
                return;
            }
            if (visited.Contains(section)) return;
            if (visited.Count >= maxVisitedSections)
            {
                Warn("Troppe sezioni Needs: dipendenze non risolte.");
                return;
            }
            if (!sections.TryGetValue(section, out var lines))
            {
                Warn("Sezione Needs locale non trovata: dipendenze non risolte.");
                return;
            }

            visited.Add(section);
            active.Add(section);
            try
            {
                // An Include changes the meaning of Needs: do not mistake an
                // external section for one with an identical local name.
                var hasInclude = lines.Any(line => TryPair(line, out var key, out _) &&
                    key.Equals("Include", StringComparison.OrdinalIgnoreCase));
                if (hasInclude)
                    Warn("Include rilevato: Needs potenzialmente esterni non risolti.");

                foreach (var line in lines)
                {
                    if (!TryPair(line, out var key, out var value)) continue;
                    if (key.Equals("Include", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (key.Equals("Needs", StringComparison.OrdinalIgnoreCase))
                    {
                        if (hasInclude)
                            continue;
                        foreach (var token in value.Split(','))
                        {
                            var target = Expand(token.Trim());
                            if (!SafeToken(target))
                            {
                                Warn("Sezione Needs non valida o non interpretabile.");
                                continue;
                            }
                            Visit(target, depth + 1);
                        }
                        continue;
                    }
                    if (key.Equals("CopyINF", StringComparison.OrdinalIgnoreCase))
                    {
                        Warn("CopyINF rilevato: file INF aggiuntivi non risolti.");
                        continue;
                    }
                    if (key.Equals("AddService", StringComparison.OrdinalIgnoreCase))
                    {
                        var fields = value.Split(',');
                        if (fields.Length >= 3)
                        {
                            var service = Expand(fields[2].Trim());
                            if (SafeToken(service) &&
                                sections.TryGetValue(service, out var serviceLines) &&
                                serviceLines.Any(line => TryPair(line, out var directive, out _) &&
                                    directive.Equals("ServiceBinary", StringComparison.OrdinalIgnoreCase)))
                                Warn("ServiceBinary in [" + service +
                                    "]: destinazione del servizio; sorgente non deducibile.");
                            else
                                Warn("AddService: sezione servizio non risolta o senza ServiceBinary.");
                        }
                        else
                            Warn("AddService: sezione servizio non determinabile.");
                        continue;
                    }
                    if (key.Equals("ServiceBinary", StringComparison.OrdinalIgnoreCase))
                    {
                        Warn("ServiceBinary in [" + section +
                            "]: destinazione del servizio; sorgente non deducibile.");
                        continue;
                    }
                    if (!key.Equals("CopyFiles", StringComparison.OrdinalIgnoreCase)) continue;
                    encounteredCopy = true;
                    foreach (var field in value.Split(','))
                    {
                        var target = Expand(field.Trim());
                        if (target.StartsWith('@'))
                        {
                            AddReference(target[1..], section, "CopyFiles");
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
                            AddReference(source, section, "CopyFiles");
                        }
                    }
                }
            }
            finally
            {
                active.Remove(section);
            }
        }

        foreach (var section in sections.Keys)
        {
            if (IsInstallSection(section, installs, arch))
                Visit(section, 0);
        }

        if (!encounteredCopy)
            Warn("Nessun CopyFiles interpretato: non significa che il driver non richieda file.");

        var results = requested.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry =>
            {
                var name = entry.Key;
                var origin = entry.Value;
                if (!sources.TryGetValue(name, out var source) || source.RelativePath is null)
                    return new Reference(name, null, Presence.Unresolved, origin);
                return new Reference(name, source.RelativePath,
                    onDisk.Contains(source.RelativePath) ? Presence.Present : Presence.Missing, origin);
            }).ToArray();
        return new Report(results, warnings.ToArray());

        void AddReference(string file, string section, string directive)
        {
            file = Expand(file);
            if (!SafeFilename(file))
            {
                Warn("Riferimento CopyFiles con nome non sicuro o variabile INF.");
                return;
            }
            if (requested.ContainsKey(file)) return;
            requested.Add(file, section + " / " + directive);
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
        // Preserve the original 12,000 *physical* line ceiling even when
        // continuation joins multiple lines into one logical directive.
        // Use the shared, conservative INF reader: a quoted semicolon is
        // data, not a comment, and CopyFiles/Needs may span lines.
        if (inf.Count(ch => ch == '\n') >= 12000)
            throw new InvalidDataException("Troppi record INF.");
        foreach (var line in InfLogicalLines.Read(inf))
        {
            if (line.Length > 4096)
                throw new InvalidDataException("Riga INF eccessiva.");
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

    // Parse INF comma-separated fields without splitting a quoted disk
    // description, tag/CAB filename or subdirectory. A bare Split(',')
    // shifts the fourth SourceDisksNames field when a description contains
    // a comma; it also misses a CAB tag enclosed in quotes and can turn
    // a compressed, unverified payload into a false "Present" source.
    // The logical-line reader already rejects unbalanced quote pairs.
    // Limit the field count independently and fail closed on ambiguous
    // quote placements; no filesystem path is formed by this parser.
    private static string[]? SplitInfFields(string value)
    {
        var fields = new List<string>(5);
        var field = new StringBuilder();
        var insideQuotes = false;
        var afterQuote = false;

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (insideQuotes)
            {
                if (ch != '"')
                {
                    field.Append(ch);
                    continue;
                }
                if (i + 1 < value.Length && value[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                    continue;
                }
                insideQuotes = false;
                afterQuote = true;
                continue;
            }

            if (ch == ',')
            {
                if (fields.Count >= 7)
                    return null;
                fields.Add(field.ToString().Trim());
                field.Clear();
                afterQuote = false;
                continue;
            }
            if (ch == '"')
            {
                // Quotes may start a field after whitespace, never appear
                // in the middle of an unquoted path or after a closing
                // quote. Doubled quotes are only valid inside quotes.
                if (afterQuote || field.ToString().Trim().Length != 0)
                    return null;
                insideQuotes = true;
                continue;
            }
            if (afterQuote && !char.IsWhiteSpace(ch))
                return null;

            field.Append(ch);
        }
        if (insideQuotes)
            return null;
        fields.Add(field.ToString().Trim());
        return fields.ToArray();
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
        if (text.Length > 120 || text.Contains(':') || text.Contains('%') ||
            text.Contains("..", StringComparison.Ordinal))
            return false;

        // A single leading slash is a legacy INF source-root convention.
        // Two separators (including mixed \\/ or /\\) are UNC/device-root
        // syntax, NOT a relative OEM folder. TrimStart used to discard
        // both and turn \\\\server\\share into server/share: a local
        // namesake could then be reported Present and proposed to SignTool.
        // Do not access the alleged network path or include it in warnings.
        if (text.Length >= 2 &&
            text[0] is ('\\' or '/') &&
            text[1] is ('\\' or '/'))
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
