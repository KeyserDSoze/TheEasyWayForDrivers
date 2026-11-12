using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #401: never treat a truncated, partially consumed or changing INF
/// stream as trustworthy read-only CopyFiles/dependency evidence.
/// </summary>
public sealed class OemInfBoundedStreamIntegrityTests
{
    private const string NvidiaInf = """
        [Version]
        Signature="$Windows NT$"
        [DefaultInstall]
        CopyFiles=@nvidia.sys
        [SourceDisksNames]
        1=Media,,,drivers
        [SourceDisksFiles]
        nvidia.sys=1
        """;

    [Fact]
    public void AlreadyAdvancedInputIsNotInterpretedAsCompleteInf()
    {
        var prefix = Encoding.UTF8.GetBytes("; earlier data\n");
        var rest = Encoding.UTF8.GetBytes(NvidiaInf);
        using var stream = new MemoryStream(
            [.. prefix, .. rest]);
        stream.Position = prefix.Length;

        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.ReadBounded(stream));
        stream.Position = prefix.Length;
        Assert.Throws<InvalidDataException>(() =>
            InfDependencyPreview.AnalyzeBounded(stream,
                ["drivers/nvidia.sys"], "x64"));
    }

    [Fact]
    public void PrematureEofRejectsEvenIfTruncatedPrefixIsValidInf()
    {
        var prefix = Encoding.UTF8.GetBytes(NvidiaInf);
        var suffix = Encoding.UTF8.GetBytes(
            "\n[SourceDisksFiles.amd64]\nnvidia.sys=2\n");
        using var stream = new StopsAfterPrefixStream(
            [.. prefix, .. suffix], prefix.Length);

        Assert.Throws<InvalidDataException>(() =>
            InfDependencyPreview.AnalyzeBounded(stream,
                ["drivers/nvidia.sys"], "x64"));
    }

    [Fact]
    public void FileGrowingUnderFourMegabytesDuringReadIsRejected()
    {
        using var stream = new AppendsAfterFirstReadStream(
            Encoding.UTF8.GetBytes(NvidiaInf));
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.ReadBounded(stream));
    }

    [Fact]
    public void ShrinkingStreamLengthEvenAfterReadingFullOriginalDataIsRejected()
    {
        using var stream = new ChangesReportedLengthAfterReadStream(
            Encoding.UTF8.GetBytes(NvidiaInf));
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.ReadBounded(stream));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteUtf8AndUtf16BeInputsStillProduceReadOnlyEvidence(
        bool useUtf16)
    {
        byte[] bytes;
        if (useUtf16)
        {
            var unicode = new UnicodeEncoding(
                bigEndian: true, byteOrderMark: true,
                throwOnInvalidBytes: true);
            bytes = [.. unicode.GetPreamble(), .. unicode.GetBytes(NvidiaInf)];
        }
        else
            bytes = Encoding.UTF8.GetBytes(NvidiaInf);

        using var stream = new MemoryStream(bytes);
        var result = InfDependencyPreview.AnalyzeBounded(stream,
            ["drivers/nvidia.sys"], "x64");

        Assert.Equal(InfDependencyPreview.Presence.Present,
            Assert.Single(result.References).Status);
        Assert.Equal(1, result.PresentCount);
        Assert.Equal(0, result.UnresolvedCount);
        Assert.False(result.IsCompletePackage);
        Assert.True(result.RequiresManualReview);
    }

    private sealed class StopsAfterPrefixStream(
        byte[] contents, int readableBytes) : MemoryStream(contents)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = readableBytes - Position;
            if (remaining <= 0)
                return 0;
            return base.Read(buffer, offset,
                (int)Math.Min(count, remaining));
        }
    }

    private sealed class AppendsAfterFirstReadStream : MemoryStream
    {
        private bool _appended;

        public AppendsAfterFirstReadStream(byte[] original)
        {
            Write(original);
            Position = 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            if (!_appended)
            {
                _appended = true;
                var position = Position;
                Position = Length;
                Write(Encoding.UTF8.GetBytes("\n; changed after metadata\n"));
                Position = position;
            }
            return read;
        }
    }

    private sealed class ChangesReportedLengthAfterReadStream(
        byte[] contents) : MemoryStream(contents)
    {
        private bool _read;

        public override long Length =>
            _read ? base.Length - 1 : base.Length;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            _read = true;
            return read;
        }
    }
}
