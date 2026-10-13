using System.Text;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>Bounded cabinet directory parser; never reads CFDATA or decompresses.</summary>
public static class OemCabIndex
{
    public sealed record Entry(string Path, uint DeclaredSize);
    public sealed record Report(IReadOnlyList<Entry> Entries)
    {
        public bool IsAuthenticated => false;
        public bool IsInstallable => false;
    }

    public static Report Inspect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek || stream.Length < 36 ||
            stream.Length > OemArchiveInspector.MaxArchiveBytes)
            throw new InvalidDataException("Dimensioni CAB non valide.");
        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != 0x4643534d) throw new InvalidDataException("Firma MSCF assente.");
        if (reader.ReadUInt32() != 0) throw new InvalidDataException("Header CAB non supportato.");
        uint cabinetSize = reader.ReadUInt32();
        if (reader.ReadUInt32() != 0) throw new InvalidDataException("Header CAB non supportato.");
        uint fileOffset = reader.ReadUInt32();
        if (reader.ReadUInt32() != 0) throw new InvalidDataException("Header CAB non supportato.");
        if (reader.ReadByte() != 3 || reader.ReadByte() != 1)
            throw new InvalidDataException("Versione CAB non supportata.");
        ushort folders = reader.ReadUInt16();
        ushort files = reader.ReadUInt16();
        ushort flags = reader.ReadUInt16();
        reader.ReadUInt16(); // setID
        reader.ReadUInt16(); // iCabinet
        if (flags != 0 || folders == 0 || folders > 64 || files > 2048 ||
            cabinetSize != stream.Length || fileOffset < 36 ||
            fileOffset >= cabinetSize || 36L + folders * 8L > fileOffset)
            throw new InvalidDataException("CAB multipart, riservato o fuori dai limiti.");
        // With flags=0, folders start immediately after the 36-byte CFHEADER.
        for (int i = 0; i < folders; i++)
        {
            uint dataOffset = reader.ReadUInt32();
            reader.ReadUInt16(); // number of CFDATA blocks
            ushort compression = reader.ReadUInt16();
            if (dataOffset >= cabinetSize || (compression & 0x000f) > 3)
                throw new InvalidDataException("Descrittore cartella CAB non valido.");
        }
        stream.Position = fileOffset;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Entry>();
        long total = 0;
        for (int i = 0; i < files; i++)
        {
            if (stream.Position + 16 > cabinetSize) throw new InvalidDataException("CFFILE troncato.");
            uint size = reader.ReadUInt32();
            reader.ReadUInt32(); // uncompressed offset, not dereferenced
            ushort folderIndex = reader.ReadUInt16();
            reader.ReadUInt16(); // date
            reader.ReadUInt16(); // time
            reader.ReadUInt16(); // attributes
            if (folderIndex >= folders || size > OemArchiveInspector.MaxEntryBytes)
                throw new InvalidDataException("File CAB fuori dai limiti o folder split.");
            total = checked(total + size);
            if (total > OemArchiveInspector.MaxDeclaredBytes)
                throw new InvalidDataException("Dimensioni aggregate CAB eccessive.");
            var bytes = new List<byte>();
            bool terminated = false;
            for (int n = 0; n <= 260 && stream.Position < cabinetSize; n++)
            {
                byte character = reader.ReadByte();
                if (character == 0) { terminated = true; break; }
                bytes.Add(character);
            }
            if (!terminated) throw new InvalidDataException("Nome CFFILE non terminato.");
            string name;
            try { name = new UTF8Encoding(false, true).GetString(bytes.ToArray()); }
            catch (DecoderFallbackException ex)
            { throw new InvalidDataException("Encoding CAB non gestito.", ex); }
            name = name.Replace('\\', '/');
            if (!SafeName(name) || !names.Add(name))
                throw new InvalidDataException("Percorso CAB non sicuro o duplicato.");
            result.Add(new Entry(name, size));
        }
        return new Report(result);
    }

    private static bool SafeName(string name) =>
        name.Length is > 0 and <= 260 &&
        !name.StartsWith('/') && !name.Contains(':') &&
        !name.Contains('%') && !name.Any(char.IsControl) &&
        name.Split('/').All(part => part.Length is > 0 and <= 120 &&
            part is not "." and not ".." &&
            part.All(c => char.IsLetterOrDigit(c) ||
                c is '_' or '.' or '-' or '+' or '(' or ')' or ' '));
}
