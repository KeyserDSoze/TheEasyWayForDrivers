using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #400: the opt-in Desktop audit must decode the same bounded INF bytes
/// as ZIP/CAB/folder OEM evidence. It must not create installable offers.
/// </summary>
public sealed class InfDependencyBoundedManualAuditTests
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

    private const string KillerInf = """
        [Version]
        Signature="$Windows NT$"
        [DefaultInstall]
        CopyFiles=@killer.sys
        [SourceDisksNames]
        1="OEM; notebook",,,drivers
        [SourceDisksFiles]
        killer.sys=1
        [Strings]
        Device="Café Killer"
        """;

    [Fact]
    public void ManualPreviewReadsNvidiaInfLargerThanOldOneMegabyteLimit()
    {
        // Synthetic large INF: all padding is comment text, not driver IDs.
        // Each logical INF directive stays below the existing 4,096-char
        // line limit and 12,000-physical-line parser ceiling.
        var text = NvidiaInf + "\n" + string.Concat(Enumerable.Repeat(
            "; OEM catalogue padding " + new string('X', 1000) + "\n", 1200));
        var bytes = Encoding.UTF8.GetBytes(text);
        Assert.True(bytes.Length > 1024 * 1024);
        Assert.True(bytes.Length < OemZipInfEvidence.MaxInfBytes);

        using var stream = new MemoryStream(bytes);
        var report = InfDependencyPreview.AnalyzeBounded(stream,
            ["drivers/nvidia.sys"], "amd64");

        var reference = Assert.Single(report.References);
        Assert.Equal("nvidia.sys", reference.SourceName);
        Assert.Equal("drivers/nvidia.sys", reference.RelativePath);
        Assert.Equal(InfDependencyPreview.Presence.Present, reference.Status);
        Assert.Equal(1, report.PresentCount);
        Assert.Equal(0, report.UnresolvedCount);
        Assert.False(report.IsCompletePackage);
        Assert.True(report.RequiresManualReview);
    }

    [Fact]
    public void ManualPreviewReadsUtf16BigEndianKillerInf()
    {
        var encoding = new UnicodeEncoding(bigEndian: true,
            byteOrderMark: true, throwOnInvalidBytes: true);
        using var stream = new MemoryStream(
            [.. encoding.GetPreamble(), .. encoding.GetBytes(KillerInf)]);
        var report = InfDependencyPreview.AnalyzeBounded(stream,
            ["drivers/killer.sys"], "x64");

        var reference = Assert.Single(report.References);
        Assert.Equal("killer.sys", reference.SourceName);
        Assert.Equal(InfDependencyPreview.Presence.Present, reference.Status);
        Assert.Equal("drivers/killer.sys", reference.RelativePath);
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void ManualPreviewRecoversLegacyAnsiOnlyWithSafeAsciiGrammar()
    {
        // 0xE9 in a localized [Strings] VALUE is not legal standalone UTF-8;
        // the shared decoder recovers only ASCII directive/file grammar.
        using var stream = new MemoryStream(
            Encoding.Latin1.GetBytes(KillerInf));
        var report = InfDependencyPreview.AnalyzeBounded(stream,
            ["drivers/killer.sys"], "x64");

        Assert.Equal(InfDependencyPreview.Presence.Present,
            Assert.Single(report.References).Status);
        Assert.Equal(0, report.MissingCount);
        Assert.False(report.IsCompletePackage);
        Assert.DoesNotContain("Café", report.Summary);
    }

    [Fact]
    public void MissingFileStillMeansPotentialMissingNotAnInstallPermission()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(NvidiaInf));
        var report = InfDependencyPreview.AnalyzeBounded(stream, [], "x64");

        Assert.Equal(InfDependencyPreview.Presence.Missing,
            Assert.Single(report.References).Status);
        Assert.Equal(1, report.MissingCount);
        Assert.Equal(0, report.PresentCount);
        Assert.False(report.IsCompletePackage);
        Assert.True(report.RequiresManualReview);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x00, 0x00, 0xFE, 0xFF })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0xD8 })]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0xC3, 0x28 })]
    public void MalformedAndUtf32InfBytesNeverEnterManualPreview(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() =>
            InfDependencyPreview.AnalyzeBounded(stream, [], "x64"));
    }

    [Fact]
    public void FourMegabyteBoundIsStillFailClosedForManualPreview()
    {
        using var stream = new MemoryStream(
            new byte[OemZipInfEvidence.MaxInfBytes + 1]);
        Assert.Throws<InvalidDataException>(() =>
            InfDependencyPreview.AnalyzeBounded(stream, [], "x64"));
    }

    [Fact]
    public void BinaryControlByteInOtherwiseValidInfIsNeverAccepted()
    {
        var content = Encoding.UTF8.GetBytes(NvidiaInf + "\n");
        var bytes = content.Concat(new byte[] { 0x00 }).ToArray();
        using var stream = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() =>
            InfDependencyPreview.AnalyzeBounded(stream,
                ["drivers/nvidia.sys"], "x64"));
    }
}
