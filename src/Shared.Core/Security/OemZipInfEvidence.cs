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
    public const int MaxInfFiles = 32;
    public const int MaxInfBytes = 1024 * 1024;
    public const int MaxTotalInfBytes = 8 * 1024 * 1024;

    public sealed record InfEntry(string Path, IReadOnlyList<InfModelEvidence.DeclaredId> DeclaredIds,
        string Note);
    public sealed record Report(IReadOnlyList<InfEntry> InfFiles, IReadOnlyList<string> Warnings)
    {
        public bool IsInstallable => false;
        public bool IsVerified => false;
        public int DeclaredIdCount => InfFiles.Sum(item => item.DeclaredIds.Count);
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
            var bytes = sink.ToArray();
            // UTF-16 LE BOM is common for Windows INF; do not guess legacy ANSI
            // codepages or accept malformed Unicode as reliable evidence.
            string text;
            try
            {
                text = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
                    ? new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2)
                    : new UTF8Encoding(true, true).GetString(bytes,
                        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0,
                        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes.Length - 3 : bytes.Length);
            }
            catch (DecoderFallbackException)
            {
                warnings.Add(candidate.Path + ": encoding INF non supportato.");
                output.Add(new InfEntry(candidate.Path, [], "Encoding non risolto"));
                continue;
            }
            var ids = InfModelEvidence.ReadDeclaredIds(text);
            output.Add(new InfEntry(candidate.Path, ids,
                "ID dichiarati (non match Windows, firma o installabilità)"));
        }
        return new Report(output, warnings);
    }
}
