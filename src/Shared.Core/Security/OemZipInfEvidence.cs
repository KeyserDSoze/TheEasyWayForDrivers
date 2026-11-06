using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Bounded in-memory preview of declared PnP IDs in ZIP INF entries.
/// Not SetupAPI, authenticity verification, or permission to install.
/// </summary>
public static class OemZipInfEvidence
{
    public const int MaxInfFiles = 96;
    public const int MaxInfBytes = 4 * 1024 * 1024;
    public const int MaxTotalInfBytes = 24 * 1024 * 1024;

    public sealed record InfEntry(string Path, IReadOnlyList<InfModelEvidence.DeclaredId> DeclaredIds,
        string Note, bool IsReadable = true, OemInfTextDecoder.Kind? TextEncoding = null);
    public sealed record Report(IReadOnlyList<InfEntry> InfFiles, IReadOnlyList<string> Warnings)
    {
        public bool IsInstallable => false;
        public bool IsVerified => false;
        public int DeclaredIdCount => InfFiles.Sum(item => item.DeclaredIds.Count);
        public int RejectedInfCount => InfFiles.Count(item => !item.IsReadable);
        public int ReadableInfCount => InfFiles.Count - RejectedInfCount;
        public int InfWithDeclaredIds => InfFiles.Count(item => item.DeclaredIds.Count > 0);
        public int LegacyAnsiCount => InfFiles.Count(item =>
            item.TextEncoding == OemInfTextDecoder.Kind.LegacyAnsiAsciiOnly);
    }

    public static Report Inspect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        // Reuse the complete inventory and path/symlink/ratio checks FIRST.
        var inventory = OemArchiveInspector.Inspect(stream);
        if (inventory.ArchiveFormat != OemArchiveInspector.Format.Zip)
            return new Report([], ["Formato non ZIP: INF interni non ispezionati."]);
        var candidates = inventory.Entries
            .Where(item => item.Kind == OemArchiveInspector.FileKind.Inf)
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (candidates.Length > MaxInfFiles)
            throw new InvalidDataException("Troppi file INF per ispezione in memoria.");
        var warnings = new List<string>();
        var output = new List<InfEntry>();
        long total = 0;
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var candidate in candidates)
        {
            if (candidate.DeclaredBytes > MaxInfBytes)
                throw new InvalidDataException("INF ZIP oltre il limite di lettura.");
            var entry = zip.GetEntry(candidate.Path)
                ?? throw new InvalidDataException("Elemento INF ZIP non più disponibile.");
            using var source = entry.Open();
            using var sink = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
            {
                total = checked(total + read);
                if (sink.Length + read > MaxInfBytes || total > MaxTotalInfBytes)
                    throw new InvalidDataException("Dimensioni INF decompressed oltre i limiti.");
                sink.Write(buffer, 0, read);
            }
            OemInfTextDecoder.Result decoded;
            try
            {
                decoded = OemInfTextDecoder.Decode(sink.ToArray());
            }
            catch (InvalidDataException)
            {
                // Don't print untrusted paths, names, hardware IDs or private
                // directories in the aggregate report.
                warnings.Add("Un file INF non è stato decodificato in modo attendibile.");
                output.Add(new InfEntry(candidate.Path, [],
                    "INF ignorato: codifica/dati non attendibili", IsReadable: false));
                continue;
            }

            IReadOnlyList<InfModelEvidence.DeclaredId> ids;
            try
            {
                // Syntax failures are isolated to THIS INF, not the entire
                // OEM package. No partial model IDs from a malformed file
                // may be treated as valid evidence for a device.
                ids = InfModelEvidence.ReadDeclaredIds(decoded.Text);
            }
            catch (InvalidDataException)
            {
                warnings.Add("Un file INF contiene direttive malformate; " +
                    "le altre sorgenti del pacchetto restano analizzabili.");
                output.Add(new InfEntry(candidate.Path, [],
                    "INF ignorato: direttive malformate", IsReadable: false,
                    TextEncoding: decoded.Encoding));
                continue;
            }

            if (ids.Count >= InfModelEvidence.MaximumPreviewIds)
                warnings.Add(
                    "Un INF ha almeno 500 ID nella sola anteprima; " +
                    "l'assenza di un ID da questa lista non prova incompatibilità. " +
                    "Usare il confronto mirato con l'inventario PnP.");
            var info = decoded.IsLegacyFallback
                ? "ANSI 8-bit: solo sintassi/ID ASCII riconosciuti, codepage non determinata."
                : "ID dichiarati (non match Windows, firma o installabilità).";
            output.Add(new InfEntry(candidate.Path, ids, info,
                TextEncoding: decoded.Encoding));
        }
        return new Report(output, warnings);
    }
}
