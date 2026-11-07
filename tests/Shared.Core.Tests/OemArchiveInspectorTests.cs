using System.Buffers.Binary;
using System.IO.Compression;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemArchiveInspectorTests
{
    private static MemoryStream BuildZip(params string[] names)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in names)
            {
                var entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("test");
            }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void ListsInfCatSysButNeverMarksInstallable()
    {
        using var zip = BuildZip("drivers/main.inf", "drivers/main.cat", "drivers/main.sys", "README.txt");
        var report = OemArchiveInspector.Inspect(zip);
        Assert.Equal(OemArchiveInspector.Format.Zip, report.ArchiveFormat);
        Assert.Equal(4, report.Entries.Count);
        Assert.Contains(report.Entries, e => e.Kind == OemArchiveInspector.FileKind.Inf);
        Assert.Contains(report.Entries, e => e.Kind == OemArchiveInspector.FileKind.Cat);
        Assert.Contains(report.Entries, e => e.Kind == OemArchiveInspector.FileKind.Sys);
        Assert.False(report.IsInstallable);
        Assert.False(report.ContentsAuthenticated);
        Assert.True(report.RequiresManualReview);
    }

    [Theory]
    [InlineData("../outside.sys")]
    [InlineData("/absolute.sys")]
    [InlineData("C:/windows/file.sys")]
    [InlineData("a/./same.inf")]
    [InlineData("a//double.cat")]
    public void RejectsUnsafeZipMemberName(string name)
    {
        using var zip = BuildZip(name);
        Assert.Throws<InvalidDataException>(() => OemArchiveInspector.Inspect(zip));
    }

    [Fact]
    public void RejectsCaseInsensitiveDuplicateNames()
    {
        using var zip = BuildZip("drivers/X.inf", "drivers/x.inf");
        Assert.Throws<InvalidDataException>(() => OemArchiveInspector.Inspect(zip));
    }

    [Fact]
    public void CabIsIdentifiedButNotEnumerated()
    {
        using var stream = new MemoryStream(new byte[] { 0x4d, 0x53, 0x43, 0x46, 0, 0, 0, 0 });
        var report = OemArchiveInspector.Inspect(stream);
        Assert.Equal(OemArchiveInspector.Format.Cab, report.ArchiveFormat);
        Assert.Empty(report.Entries);
        Assert.False(report.IsInstallable);
    }

    // A ZIP's central directory may sit after a large binary payload.
    // Simulate the padding with a seekable sparse stream: no GiB allocation,
    // no disk staging and no decompression of any large SYS/DLL/EXE.
    [Fact]
    public void InspectsZipOverOneGiBAndCanStillReadSmallInf()
    {
        using var smallZip = BuildZip("drivers/graphics.inf");
        var bytes = smallZip.ToArray();
        var centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(bytes.Length - 6, 4));
        var prefix = bytes[..checked((int)centralOffset)];
        var centralDirectoryAndEnd = bytes[(int)centralOffset..];
        const long centralStart = 1024L * 1024 * 1024 + 65536;
        BinaryPrimitives.WriteUInt32LittleEndian(
            centralDirectoryAndEnd.AsSpan(centralDirectoryAndEnd.Length - 6, 4),
            checked((uint)centralStart));

        using var largeZip = new SparseStream(
            prefix, centralDirectoryAndEnd, centralStart);
        Assert.True(largeZip.Length > OemArchiveInspector.MaxArchiveBytes);
        Assert.True(largeZip.Length < OemArchiveInspector.MaxZipArchiveBytes);

        var report = OemArchiveInspector.Inspect(largeZip);
        var item = Assert.Single(report.Entries);
        Assert.Equal("drivers/graphics.inf", item.Path);
        Assert.Equal(OemArchiveInspector.FileKind.Inf, item.Kind);
        Assert.False(report.IsInstallable);
        Assert.False(report.ContentsAuthenticated);

        largeZip.Position = 0;
        using var archive = new ZipArchive(
            largeZip, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(
            archive.GetEntry(item.Path)!.Open());
        Assert.Equal("test", reader.ReadToEnd());
    }

    [Theory]
    [InlineData("MSCF")]
    [InlineData("MZxx")]
    [InlineData("ABCD")]
    public void NonZipOverOneGiBRemainsRejected(string signature)
    {
        using var large = new SparseStream(
            System.Text.Encoding.ASCII.GetBytes(signature),
            [1, 2, 3, 4], OemArchiveInspector.MaxArchiveBytes + 65536);
        Assert.Throws<InvalidDataException>(() =>
            OemArchiveInspector.Inspect(large));
    }

    [Fact]
    public void AboveFourGiBRejectedWithoutReadingCentralDirectory()
    {
        using var huge = new SparseStream(
            [0x50, 0x4b, 0x03, 0x04], [0, 0, 0, 0],
            OemArchiveInspector.MaxZipArchiveBytes + 1);
        Assert.Throws<InvalidDataException>(() =>
            OemArchiveInspector.Inspect(huge));
    }

    [Fact]
    public void LargeZipAllowanceDoesNotRaiseCabOrNestedExtractionCaps()
    {
        Assert.Equal(1024L * 1024 * 1024,
            OemArchiveInspector.MaxArchiveBytes);
        Assert.Equal(512L * 1024 * 1024,
            OemArchiveInspector.MaxEntryBytes);
        Assert.Equal(2L * 1024 * 1024 * 1024,
            OemArchiveInspector.MaxDeclaredBytes);
        Assert.Equal(4L * 1024 * 1024 * 1024,
            OemArchiveInspector.MaxZipArchiveBytes);
        Assert.Equal(8L * 1024 * 1024 * 1024,
            OemArchiveInspector.MaxZipDeclaredBytes);
    }

    private sealed class SparseStream(
        byte[] prefix, byte[] tail, long tailStart) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => checked(tailStart + tail.Length);
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

        public override int Read(Span<byte> buffer)
        {
            var length = (int)Math.Min(buffer.Length, Length - _position);
            var destination = buffer[..length];
            destination.Clear();
            CopySegment(prefix, 0, destination);
            CopySegment(tail, tailStart, destination);
            _position += length;
            return length;
        }

        private void CopySegment(byte[] content, long start,
            Span<byte> destination)
        {
            var from = Math.Max(_position, start);
            var end = Math.Min(_position + destination.Length,
                start + content.Length);
            if (end > from)
                content.AsSpan(checked((int)(from - start)),
                    checked((int)(end - from)))
                    .CopyTo(destination.Slice(
                        checked((int)(from - _position)),
                        checked((int)(end - from))));
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));
        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            return Position;
        }
        public override void Flush() { }
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    [Fact]
    public void NeverExtractsArchiveContents()
    {
        using var zip = BuildZip("sample.inf");
        var before = zip.Length;
        var report = OemArchiveInspector.Inspect(zip);
        Assert.Equal(before, zip.Length);
        Assert.Single(report.Entries);
    }
}
