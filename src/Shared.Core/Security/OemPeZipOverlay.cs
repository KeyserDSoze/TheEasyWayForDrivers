using System.Buffers.Binary;
using System.IO.Compression;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Strictly read-only recognition of a standard ZIP appended after the end
/// of real PE sections (a narrow subset of OEM self-extracting EXE formats).
/// No invocation of the vendor EXE, PE loader, shell, DLL, script or installer.
/// 7z/NSIS/Inno/other proprietary EXEs remain explicitly unsupported.
/// </summary>
public static class OemPeZipOverlay
{
    public const long MaximumOverlayPrefixBytes = 16L * 1024 * 1024;
    public const int MaximumPeHeaderOffset = 1024 * 1024;
    public const int MaximumPeSections = 96;

    /// <summary>
    /// Provides a seekable view of the *appended ZIP only*. The caller owns
    /// the source stream and must keep it open until the view is disposed.
    /// ZIP paths/size/compression checks are enforced on the sliced stream
    /// through the existing OemArchiveInspector + LocalOemDriverComparison.
    /// </summary>
    public static Stream Open(Stream executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        if (!executable.CanRead || !executable.CanSeek ||
            executable.Length is < 128 or > OemArchiveInspector.MaxArchiveBytes)
            throw new InvalidDataException("EXE OEM fuori limite o non leggibile.");

        var sectionsEnd = FindLastSectionEnd(executable);
        if (sectionsEnd >= executable.Length - 30)
            throw new NotSupportedException(
                "EXE OEM senza ZIP standard dopo le sezioni PE. " +
                "7z/NSIS/Inno e installer proprietari non vengono eseguiti.");

        var searchEnd = Math.Min(executable.Length - 4,
            checked(sectionsEnd + MaximumOverlayPrefixBytes));
        var bytes = new byte[64 * 1024];
        // Keep a 3-byte overlap so PK\x03\x04 at a block boundary is found.
        var start = sectionsEnd;
        while (start <= searchEnd)
        {
            executable.Position = start;
            var available = (int)Math.Min(bytes.Length,
                executable.Length - start);
            var count = executable.Read(bytes, 0, available);
            if (count < 4)
                break;

            for (var i = 0; i <= count - 4; i++)
            {
                if (bytes[i] != 0x50 || bytes[i + 1] != 0x4B ||
                    bytes[i + 2] != 0x03 || bytes[i + 3] != 0x04)
                    continue;

                var offset = start + i;
                // Never accept a signature beyond the bounded 16MiB
                // overlay prefix, even when the final I/O block extends
                // past the inspection budget by up to 64KiB.
                if (offset > searchEnd)
                    break;
                // Never accept PK inside .text, .rsrc or another PE section.
                if (offset < sectionsEnd)
                    continue;
                var zip = new ReadOnlySegmentStream(executable, offset);
                try
                {
                    // Full existing fail-closed ZIP inventory is mandatory.
                    var report = OemArchiveInspector.Inspect(zip);
                    if (report.ArchiveFormat != OemArchiveInspector.Format.Zip)
                        throw new InvalidDataException("Overlay EXE non ZIP.");
                    zip.Position = 0;
                    return zip;
                }
                catch
                {
                    zip.Dispose();
                    // An overlay with a ZIP signature but malformed/unsafe
                    // entries is suspect: do not skip it to find a second
                    // good ZIP later in the same untrusted executable.
                    throw;
                }
            }

            if (start + count > searchEnd)
                break;
            start += count - 3;
        }
        throw new NotSupportedException(
            "Formato EXE OEM non supportato: nessun ZIP standard dopo " +
            "le sezioni PE. 7z/NSIS/Inno e installer proprietari " +
            "non vengono eseguiti o analizzati.");
    }

    private static long FindLastSectionEnd(Stream source)
    {
        source.Position = 0;
        Span<byte> dos = stackalloc byte[64];
        source.ReadExactly(dos);
        if (dos[0] != 'M' || dos[1] != 'Z')
            throw new NotSupportedException("Non è un eseguibile Windows PE.");
        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(dos[0x3C..]);
        if (peOffset < 64 || peOffset > MaximumPeHeaderOffset ||
            (long)peOffset + 24 > source.Length)
            throw new InvalidDataException("Puntatore header PE non valido.");

        source.Position = peOffset;
        using var reader = new BinaryReader(source,
            System.Text.Encoding.ASCII, leaveOpen: true);
        if (reader.ReadUInt32() != 0x00004550)
            throw new InvalidDataException("Firma PE mancante.");
        var machine = reader.ReadUInt16();
        if (machine is not (0x014C or 0x8664 or 0xAA64))
            throw new NotSupportedException("Architettura eseguibile OEM non supportata.");
        var sections = reader.ReadUInt16();
        if (sections is < 1 or > MaximumPeSections)
            throw new InvalidDataException("Numero sezioni PE non valido.");
        reader.ReadUInt32(); // Timestamp
        reader.ReadUInt32(); // COFF symbol pointer
        reader.ReadUInt32(); // COFF symbols
        var optionalHeader = reader.ReadUInt16();
        reader.ReadUInt16(); // characteristics
        var sectionTable = (long)peOffset + 24 + optionalHeader;
        if (optionalHeader is < 96 or > 1024 ||
            sectionTable + sections * 40L > source.Length)
            throw new InvalidDataException("Header o tabella sezioni PE non validi.");

        var highest = sectionTable + sections * 40L;
        for (int i = 0; i < sections; i++)
        {
            source.Position = sectionTable + 40L * i + 16;
            var rawBytes = reader.ReadUInt32();
            var fileOffset = reader.ReadUInt32();
            if (rawBytes == 0)
                continue;
            if (fileOffset == 0 ||
                (long)fileOffset + rawBytes > source.Length ||
                fileOffset < sectionTable + sections * 40L)
                throw new InvalidDataException(
                    "Sezione PE fuori dal file o sovrapposta agli header.");

            highest = Math.Max(highest, (long)fileOffset + rawBytes);
        }
        return highest;
    }

    private sealed class ReadOnlySegmentStream(Stream source, long start) : Stream
    {
        private long _position;
        private bool _disposed;

        public override bool CanRead => !_disposed && source.CanRead;
        public override bool CanSeek => !_disposed && source.CanSeek;
        public override bool CanWrite => false;
        public override long Length => !_disposed
            ? source.Length - start : throw new ObjectDisposedException(nameof(ReadOnlySegmentStream));

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > Length)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(buffer);
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));
            var readable = (int)Math.Min(count, Length - _position);
            source.Position = start + _position;
            var read = source.Read(buffer, offset, readable);
            _position += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var available = (int)Math.Min(buffer.Length, Length - _position);
            source.Position = start + _position;
            var read = source.Read(buffer[..available]);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            Position = position;
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            // The caller owns 'source'; this wrapper does not close it.
            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
