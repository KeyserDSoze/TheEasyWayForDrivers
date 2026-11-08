using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemInfTextDecoderTests
{
    private const string NvidiaId =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaAudio =
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string KillerId =
        @"PCI\VEN_8086&DEV_51F1";

    private static string Inf(string id, string version = "32.0.15.7000") =>
        $"""
        [Version]
        Signature="$Windows NT$"
        Class=Display
        DriverVer=10/04/2026,{version}
        [Manufacturer]
        %Vendor%=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        %Device%=Install,{id}
        [Strings]
        Vendor="Sample"
        Device="Sample device"
        """;

    private static byte[] Unicode(string text, bool bigEndian)
    {
        var encoder = new UnicodeEncoding(bigEndian, true, true);
        return [.. encoder.GetPreamble(), .. encoder.GetBytes(text)];
    }

    private static byte[] Legacy(string inf)
    {
        // 0xE9 is an ANSI high byte not legal as a standalone UTF-8 codepoint.
        // The legacy fallback may recover only ASCII INF grammar and PnP IDs.
        var withComment = inf.Replace("Class=Display",
            "Class=Display\n; commento con carattere " + '\u00E9')
            .Replace("Vendor=\"Sample\"", "Vendor=\"Caff" + '\u00E9' + "\"");
        return Encoding.Latin1.GetBytes(withComment);
    }

    private static MemoryStream Zip(params (string Name, byte[] Bytes)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in files)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var output = entry.Open();
                output.Write(bytes);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static DriverInfo Device(string name, string id, string version) =>
        new("PRIVATE_INSTANCE_" + name, name, "OEM", "Display", "OEM",
            version, null, "installed.inf", true, true, 0, [id], []);

    [Theory]
    [InlineData(false, OemInfTextDecoder.Kind.Utf16LittleEndian)]
    [InlineData(true, OemInfTextDecoder.Kind.Utf16BigEndian)]
    public void SupportsWindowsUnicodeInfInEitherByteOrder(
        bool bigEndian, OemInfTextDecoder.Kind expected)
    {
        var decoded = OemInfTextDecoder.Decode(
            Unicode(Inf(NvidiaId), bigEndian));
        Assert.Equal(expected, decoded.Encoding);
        Assert.Equal(Inf(NvidiaId), decoded.Text);
        Assert.False(decoded.IsLegacyFallback);
        Assert.Equal(NvidiaId,
            Assert.Single(InfModelEvidence.ReadDeclaredIds(decoded.Text)).HardwareId);
    }

    [Fact]
    public void SupportsUtf8WithAndWithoutBomForReadOnlyPreview()
    {
        var plain = OemInfTextDecoder.Decode(
            new UTF8Encoding(false, true).GetBytes(Inf(KillerId)));
        var withBom = OemInfTextDecoder.Decode(
            new UTF8Encoding(true, true).GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(Inf(KillerId))).ToArray());
        Assert.Equal(OemInfTextDecoder.Kind.Utf8, plain.Encoding);
        Assert.Equal(OemInfTextDecoder.Kind.Utf8Bom, withBom.Encoding);
        Assert.Equal(plain.Text, withBom.Text);
    }

    [Fact]
    public void LegacyAnsiLocalizationsOnlyRecoverAsciiGrammarAndIds()
    {
        var decoded = OemInfTextDecoder.Decode(Legacy(Inf(NvidiaId)));
        Assert.True(decoded.IsLegacyFallback);
        Assert.Equal(OemInfTextDecoder.Kind.LegacyAnsiAsciiOnly, decoded.Encoding);
        Assert.Contains("Vendor=\"Caff\uFFFD\"", decoded.Text);
        Assert.Equal(NvidiaId,
            Assert.Single(InfModelEvidence.ReadDeclaredIds(decoded.Text)).HardwareId);
    }

    [Fact]
    public void MixedZipReportsReadableRejectedAndAnsiCountsWithoutLeakingPaths()
    {
        const string customerPrivatePath = "PRIVATE_ORDER_991";
        using var archive = Zip(
            ("nvidia/graphics.inf", Unicode(Inf(NvidiaId), bigEndian: true)),
            ("nvidia/audio.inf", Unicode(Inf(NvidiaAudio, "2.1.0.0"), bigEndian: false)),
            ("intel/wifi.inf", Legacy(Inf(KillerId, "24.0.0.0"))),
            ($"{customerPrivatePath}/bad.inf",
                [0xFF, 0xFE, 0x00, 0xD8]));

        var audit = OemZipInfEvidence.Inspect(archive);
        Assert.Equal(4, audit.InfFiles.Count);
        Assert.Equal(3, audit.ReadableInfCount);
        Assert.Equal(1, audit.RejectedInfCount);
        Assert.Equal(1, audit.LegacyAnsiCount);
        Assert.Equal(3, audit.InfWithDeclaredIds);
        Assert.DoesNotContain(customerPrivatePath, string.Join(" ", audit.Warnings));

        archive.Position = 0;
        var report = LocalOemDriverComparison.Compare(archive,
        [
            Device("RTX 4060", NvidiaId, "31.0.15.5222"),
            Device("NVIDIA HD Audio", NvidiaAudio, "1.1.0.0"),
            Device("Killer WiFi", KillerId, "23.0.1.1")
        ], "AMD64", 10, 0, 26100);

        Assert.Equal(4, report.InfExamined);
        Assert.Equal(3, report.DevicesWithEvidence);
        Assert.Equal(3, report.PotentialNewerDevices);
        Assert.Equal(1, report.RejectedInfCount);
        Assert.Equal(1, report.LegacyAnsiCount);
        Assert.Equal(3, report.InfWithDeclaredIds);
        Assert.Contains("1 scartati", report.Summary);
        Assert.Contains("1 con sintassi ANSI recuperata", report.Summary);
        Assert.Contains("il report è parziale", string.Join(" ", report.Notes));
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain(customerPrivatePath, report.Summary);
        Assert.DoesNotContain("PRIVATE_INSTANCE_", report.Summary);
    }

    [Fact]
    public void InvalidUtf8InsideHardwareIdIsNotConvertedToAValidMatch()
    {
        var bytes = Legacy(Inf(NvidiaId));
        var marker = Encoding.ASCII.GetBytes("PCI\\VEN_10DE");
        var offset = bytes.AsSpan().IndexOf(marker);
        Assert.True(offset >= 0);
        bytes[offset + 5] = 0xE9;
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(bytes));
    }

    [Fact]
    public void InvalidUtf8InsideSectionNameIsRejected()
    {
        var bytes = Legacy(Inf(NvidiaId));
        var needle = Encoding.ASCII.GetBytes("[Manufacturer]");
        var offset = bytes.AsSpan().IndexOf(needle);
        Assert.True(offset >= 0);
        bytes[offset + 1] = 0xE9;
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(bytes));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0xD8 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x00, 0x00, 0xFE, 0xFF })]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03 })]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0xC3, 0x28 })]
    public void CorruptOrUnsupportedUnicodeNeverLooksLikeInf(byte[] contents) =>
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(contents));

    [Theory]
    [InlineData(false, OemInfTextDecoder.Kind.Utf16LittleEndianNoBom)]
    [InlineData(true, OemInfTextDecoder.Kind.Utf16BigEndianNoBom)]
    public void ValidUtf16WithoutBomCanRecoverInfModelsReadOnly(
        bool bigEndian, OemInfTextDecoder.Kind expected)
    {
        var encoder = new UnicodeEncoding(bigEndian, false, true);
        var bytes = encoder.GetBytes(Inf(NvidiaId));
        var decoded = OemInfTextDecoder.Decode(bytes);
        Assert.Equal(expected, decoded.Encoding);
        Assert.False(decoded.IsLegacyFallback);
        Assert.Equal(Inf(NvidiaId), decoded.Text);
        Assert.Equal(NvidiaId, Assert.Single(
            InfModelEvidence.ReadDeclaredIds(decoded.Text)).HardwareId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoBomUtf16NvidiaKillerAndRealtekMatchesOnlyProvidedDevices(
        bool bigEndian)
    {
        const string realtekId = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
        var unicode = new UnicodeEncoding(bigEndian, false, true);
        using var archive = Zip(
            ("nvidia/gpu.inf", unicode.GetBytes(Inf(NvidiaId))),
            ("intel/killer.inf", unicode.GetBytes(Inf(KillerId, "24.0.0.0"))),
            ("realtek/audio.inf", unicode.GetBytes(Inf(realtekId, "6.0.0.0"))));

        var audit = OemZipInfEvidence.Inspect(archive);
        Assert.Equal(3, audit.ReadableInfCount);
        Assert.Equal(0, audit.RejectedInfCount);
        Assert.Equal(3, audit.InfWithDeclaredIds);
        Assert.Equal(0, audit.LegacyAnsiCount);
        Assert.All(audit.InfFiles, item =>
            Assert.Equal(bigEndian
                    ? OemInfTextDecoder.Kind.Utf16BigEndianNoBom
                    : OemInfTextDecoder.Kind.Utf16LittleEndianNoBom,
                item.TextEncoding));
        archive.Position = 0;
        var report = LocalOemDriverComparison.Compare(archive,
        [
            Device("PRIVATE_NVIDIA", NvidiaId, "31.0.15.5222"),
            Device("PRIVATE_KILLER", KillerId, "23.0.1.1"),
            Device("PRIVATE_REALTEK", realtekId, "5.0.0.0")
        ], "AMD64", 10, 0, 26100);
        Assert.Equal(3, report.InfExamined);
        Assert.Equal(3, report.DevicesWithEvidence);
        Assert.Equal(3, report.PotentialNewerDevices);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE_", report.Summary);
    }

    [Fact]
    public void UnmarkedUtf16WithNoManufacturerOrVersionCannotBeParsed()
    {
        var invalid = new[]
        {
            "[Version]\nSignature=\"$Windows NT$\"\n[Strings]\nA=B",
            "[Manufacturer]\nVendor=Models\n[Models]\nA=Install, " + NvidiaId,
            new string('A', 120) + "\n[Version]",
            "binary-not-an-INF " + new string('A', 140)
        };
        foreach (var plain in invalid)
        {
            var content = Encoding.Unicode.GetBytes(plain);
            Assert.Throws<InvalidDataException>(() =>
                OemInfTextDecoder.Decode(content));
        }
    }

    [Fact]
    public void UnmarkedUtf16WithInvalidSurrogateOrBidiNeverYieldsModels()
    {
        var invalidSurrogate = Encoding.Unicode.GetBytes(Inf(NvidiaId));
        // Replace a code unit in UTF-16LE with an unpaired high surrogate.
        var start = invalidSurrogate.AsSpan().IndexOf(
            Encoding.Unicode.GetBytes("Signature"));
        Assert.True(start >= 0);
        invalidSurrogate[start] = 0x00;
        invalidSurrogate[start + 1] = 0xD8;
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(invalidSurrogate));

        var bidi = Encoding.Unicode.GetBytes(
            Inf(KillerId) + "\n;hidden \u202E");
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(bidi));
    }

    [Fact]
    public void OddLengthOrMixedEndiannessCannotBeGuessedAsUnmarkedUtf16()
    {
        var odd = Encoding.Unicode.GetBytes(Inf(NvidiaId))
            .Concat([(byte)0]).ToArray();
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(odd));

        // Both byte lanes have too many NULs: not a valid unmarked INF.
        var ambiguous = new byte[150];
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(ambiguous));
    }

    [Fact]
    public void EmbeddedBinaryOrBidiControlInUtf8InfIsRejected()
    {
        var withNull = Encoding.UTF8.GetBytes(
            Inf(NvidiaId) + "\0embedded binary");
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(withNull));

        var withDirectionOverride = Encoding.UTF8.GetBytes(
            Inf(NvidiaId) + "\u202E");
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(withDirectionOverride));
    }

    [Fact]
    public void LegacyFallbackRequiresActualVersionHeader()
    {
        var bytes = Encoding.Latin1.GetBytes(
            "; commento " + '\u00E9' + "\n[Strings]\nTitle=\"Caff" +
            '\u00E9' + "\"");
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(bytes));
    }

    [Fact]
    public void LegacyFallbackRejectsAnsiHighByteOutsideCommentsAndStringValues()
    {
        var bytes = Legacy(Inf(NvidiaId));
        var target = Encoding.ASCII.GetBytes("Class=Display");
        var offset = bytes.AsSpan().IndexOf(target);
        Assert.True(offset >= 0);
        bytes[offset + 7] = 0xE9;
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(bytes));
    }

    [Fact]
    public void SizeBoundRejectsHugeInput()
    {
        var bytes = new byte[OemZipInfEvidence.MaxInfBytes + 1];
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(bytes));
    }
}
