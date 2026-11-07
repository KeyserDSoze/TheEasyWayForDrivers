using System.IO.Compression;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>Static, metadata-only analysis. Never extracts or executes archive members.</summary>
public static class OemArchiveInspector
{
    public enum Format { Zip, Cab, Msi, PortableExecutable, Unknown }
    public enum FileKind { Inf, Cat, Sys, Other }
    public sealed record Entry(string Path, FileKind Kind, long DeclaredBytes);
    public sealed record Report(Format ArchiveFormat, IReadOnlyList<Entry> Entries,
        IReadOnlyList<string> Warnings)
    {
        public bool IsInstallable => false;
        public bool ContentsAuthenticated => false;
        public bool RequiresManualReview => true;
    }

    public const int MaxEntries = 2048;
    public const long MaxDeclaredBytes = 2L * 1024 * 1024 * 1024;
    public const long MaxEntryBytes = 512L * 1024 * 1024;
    // CAB, PE overlays and nested extracted ZIP/CAB retain their original
    // strict limits. Large outer ZIPs are inspected by central directory:
    // only bounded INF entries are ever decompressed by downstream readers.
    public const long MaxArchiveBytes = 1024L * 1024 * 1024;
    public const long MaxZipArchiveBytes = 4L * 1024 * 1024 * 1024;
    public const long MaxZipEntryBytes = 4L * 1024 * 1024 * 1024;
    public const long MaxZipDeclaredBytes = 8L * 1024 * 1024 * 1024;

    public static Report Inspect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek || !stream.CanRead)
            throw new InvalidDataException("Archivio deve essere leggibile e seekable.");
        if (stream.Length > MaxZipArchiveBytes || stream.Length < 4)
            throw new InvalidDataException("Dimensione archivio non ammessa.");
        stream.Position = 0;
        Span<byte> signature = stackalloc byte[8];
        var bytes = stream.Read(signature);
        stream.Position = 0;
        if (bytes >= 4 && signature[..4].SequenceEqual(new byte[] { 0x50, 0x4b, 0x03, 0x04 }))
            return InspectZip(stream);
        // A large file is accepted ONLY when its CONTENT is a ZIP. Do not
        // weaken CAB/PE/unknown size gates based on a .zip filename.
        if (stream.Length > MaxArchiveBytes)
            throw new InvalidDataException("Solo ZIP possono superare 1 GiB.");
        if (bytes >= 4 && signature[..4].SequenceEqual(new byte[] { 0x4d, 0x53, 0x43, 0x46 }))
            return Opaque(Format.Cab);
        if (bytes >= 8 && signature.SequenceEqual(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }))
            return Opaque(Format.Msi);
        if (bytes >= 2 && signature[0] == 'M' && signature[1] == 'Z')
            return Opaque(Format.PortableExecutable);
        return Opaque(Format.Unknown);
    }

    private static Report Opaque(Format format) => new(format, [],
        ["Contenuto non ispezionato. Nessun file o dipendenza dichiarato verificato."]);

    /// <summary>
    /// Index ZIP entry objects by the SAME canonical path that Inspect
    /// returns. ZIP producers may preserve Windows backslashes in FullName:
    /// ZipArchive.GetEntry("foo/bar.inf") then cannot find "foo\bar.inf".
    /// Reject ambiguous / vs \ names instead of choosing an arbitrary INF.
    /// The returned entry objects are valid only while the archive is open.
    /// </summary>
    public static IReadOnlyDictionary<string, ZipArchiveEntry> CreateValidatedZipEntryIndex(
        ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Entries.Count > MaxEntries)
            throw new InvalidDataException("Numero di file ZIP eccessivo.");
        var index = new Dictionary<string, ZipArchiveEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in archive.Entries)
        {
            if (!SafePath(item.FullName))
                throw new InvalidDataException("Percorso ZIP non sicuro.");
            // ZIP Unix file type in external attributes: symbolic link.
            var unixType = (item.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000)
                throw new InvalidDataException("Link simbolici ZIP non supportati.");
            if (!index.TryAdd(item.FullName.Replace('\\', '/'), item))
                throw new InvalidDataException(
                    "File ZIP duplicati dopo normalizzazione (case insensitive).");
        }

        return index;
    }

    private static Report InspectZip(Stream stream)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var index = CreateValidatedZipEntryIndex(archive);
            var entries = new List<Entry>();
            long total = 0;
            foreach (var (canonicalPath, item) in index)
            {
                if (item.Length > MaxZipEntryBytes)
                    throw new InvalidDataException("File ZIP eccessivamente grande.");
                total = checked(total + item.Length);
                if (total > MaxZipDeclaredBytes)
                    throw new InvalidDataException("Dimensione totale dichiarata eccessiva.");
                if (item.Length > 0 &&
                    (item.CompressedLength == 0 || item.Length / Math.Max(1, item.CompressedLength) > 200))
                    throw new InvalidDataException("Rapporto compressione ZIP sospetto.");
                if (canonicalPath.EndsWith('/'))
                    continue;
                var extension = Path.GetExtension(canonicalPath);
                var kind = extension.ToLowerInvariant() switch
                {
                    ".inf" => FileKind.Inf,
                    ".cat" => FileKind.Cat,
                    ".sys" => FileKind.Sys,
                    _ => FileKind.Other
                };
                entries.Add(new Entry(canonicalPath, kind, item.Length));
            }
            return new Report(Format.Zip, entries,
                ["Indice ZIP soltanto: file non estratti né verificati. Metadati dimensione/compressione non provano sicurezza del payload."]);
        }
        catch (InvalidDataException) { throw; }
        catch (OverflowException error)
        {
            throw new InvalidDataException("Dimensioni ZIP non valide.", error);
        }
    }

    private static bool SafePath(string path)
    {
        if (path.Length is < 1 or > 260 || path[0] is '/' or '\\' ||
            path.Contains(':') || path.Contains('%') || path.Any(char.IsControl))
            return false;
        var parts = path.Replace('\\', '/').TrimEnd('/').Split('/');
        return parts.All(part => part.Length is > 0 and <= 120 &&
            part is not "." and not ".." &&
            part.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' or '-' or '+' or '(' or ')' or ' '));
    }
}
