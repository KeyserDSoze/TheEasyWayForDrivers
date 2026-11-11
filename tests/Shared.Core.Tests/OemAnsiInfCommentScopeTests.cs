using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// An ANSI semicolon inside quotes or %StringKey% is NOT an INF comment.
/// Never ignore a non-ASCII byte in a Models/Manufacturer directive merely
/// because a parser saw an earlier semicolon inside a quoted/token value.
/// </summary>
public sealed class OemAnsiInfCommentScopeTests
{
    private const string NvidiaGpu =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string KillerWifi =
        @"PCI\VEN_8086&DEV_51F1";
    private const string RealtekAudio =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static string Header =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "Class=Display\nDriverVer=10/11/2026,32.0.16.2000\n" +
        "[Manufacturer]\n%Vendor%=Models,NTamd64\n" +
        "[Models.NTamd64]\n";

    private static string LocalizedFooter =>
        "[Strings]\nVendor=\"Caff\u00E9; periferiche\"\n" +
        "Model=\"Scheda; grafic\u00E1\"\n";

    private static byte[] AsLegacyAnsi(string text) =>
        Encoding.Latin1.GetBytes(text);

    private static DriverInfo Device(string instance, string hardwareId) =>
        new(instance, "PRIVATE DEVICE NAME", "Microsoft", "Display",
            "Microsoft", "1.0", null, "PRIVATE_INF_FILE.inf",
            true, true, 0, [hardwareId], []);

    [Theory]
    [InlineData("%Model;Variant%")]
    [InlineData("\"Model;Variant\"")]
    public void SemicolonInsideModelsKeyCannotHideNonAsciiHardwareId(
        string modelKey)
    {
        // First declaration is a real NVIDIA GPU; the second alias is
        // corrupted ANSI syntax. A naive IndexOf(';') in the ANSI decoder
        // would treat the semicolon INSIDE the key as a comment, discard
        // the malformed suffix, and accept this INF as positive evidence.
        var inf = Header +
            $"{modelKey}=Install,{NvidiaGpu},{KillerWifi}\u00E9\n" +
            LocalizedFooter;
        var bytes = AsLegacyAnsi(inf);

        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(bytes));
        // An unrelated valid GPU ID before the malformed alias is never
        // enough to accept the whole corrupted directive.
        Assert.True(bytes.AsSpan().IndexOf(
            Encoding.Latin1.GetBytes(NvidiaGpu)) >= 0);
    }

    [Fact]
    public void SemicolonInsideManufacturerTokenCannotHideNonAsciiModelsReference()
    {
        var inf = "[Version]\nSignature=\"$Windows NT$\"\n" +
            "[Manufacturer]\n%Vendor;Sub%=Models,NTamd64\u00E9\n" +
            "[Models.NTamd64]\n%Model%=Install," + NvidiaGpu + "\n" +
            LocalizedFooter;
        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(AsLegacyAnsi(inf)));
    }

    [Fact]
    public void NonAsciiAfterQuotedSemicolonInVersionIsNeverAComment()
    {
        var inf = Header.Replace("Class=Display",
            "Class=\"Display; legacy\u00E9\"") +
            $"%Model%=Install,{NvidiaGpu}\n" + LocalizedFooter;

        Assert.Throws<InvalidDataException>(() =>
            OemInfTextDecoder.Decode(AsLegacyAnsi(inf)));
    }

    [Theory]
    [InlineData(NvidiaGpu)]
    [InlineData(KillerWifi)]
    [InlineData(RealtekAudio)]
    public void TrueAnsiCommentsAndLocalizedStringValuesRetainOemModelEvidence(
        string modelId)
    {
        var inf = Header +
            $"%Model;Variant%=Install,{modelId} ; commento \u00E9\n" +
            LocalizedFooter;
        var result = OemInfTextDecoder.Decode(AsLegacyAnsi(inf));

        Assert.Equal(OemInfTextDecoder.Kind.LegacyAnsiAsciiOnly,
            result.Encoding);
        Assert.True(result.IsLegacyFallback);
        Assert.Contains("Caff\uFFFD; periferiche", result.Text);
        Assert.Contains("Scheda; grafic\uFFFD", result.Text);
        Assert.Equal(modelId, Assert.Single(
            InfModelEvidence.ReadMatchingIds(
                result.Text, [Device("PRIVATE_INSTANCE", modelId)])).HardwareId);
        Assert.Equal(2, InfModelEvidence.MatchStrength(
            Device("PRIVATE_INSTANCE", modelId),
            InfModelEvidence.ReadDeclaredIds(result.Text)));
        Assert.DoesNotContain("PRIVATE_INSTANCE", result.Text);
    }

    [Fact]
    public void RealCommentsBeforeNonAsciiNeverCauseFalseRejects()
    {
        var inf = Header +
            $"%Model%=Install,{NvidiaGpu} ; \u00E9 in real comment\n" +
            LocalizedFooter;
        var result = OemInfTextDecoder.Decode(AsLegacyAnsi(inf));
        Assert.Equal(NvidiaGpu, Assert.Single(
            InfModelEvidence.ReadDeclaredIds(result.Text)).HardwareId);
    }

    [Fact]
    public void CorruptAnsiInsideZipIsCountedAsRejectedNotAsVerifiedNvidia()
    {
        var malformed = AsLegacyAnsi(Header +
            $"%Model;Name%=Install,{NvidiaGpu},{KillerWifi}\u00E9\n" +
            LocalizedFooter);
        var legitimate = AsLegacyAnsi(Header +
            $"%Model;Name%=Install,{RealtekAudio} ; \u00E9 comment\n" +
            LocalizedFooter);
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip,
                   ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in new[]
            {
                ("PRIVATE_ORDER_991/corrupt.inf", malformed),
                ("realtek/audio.inf", legitimate)
            })
            {
                var entry = archive.CreateEntry(name,
                    CompressionLevel.NoCompression);
                using var output = entry.Open();
                output.Write(bytes);
            }
        }

        zip.Position = 0;
        var audit = OemZipInfEvidence.Inspect(zip);
        Assert.Equal(1, audit.ReadableInfCount);
        Assert.Equal(1, audit.RejectedInfCount);
        Assert.Equal(1, audit.LegacyAnsiCount);
        Assert.Equal(1, audit.InfWithDeclaredIds);
        Assert.DoesNotContain("PRIVATE_ORDER_991",
            string.Join(" ", audit.Warnings));

        zip.Position = 0;
        var comparison = LocalOemDriverComparison.Compare(zip,
        [
            Device("PRIVATE_GPU_INSTANCE", NvidiaGpu),
            Device("PRIVATE_AUDIO_INSTANCE", RealtekAudio)
        ], "AMD64", 10, 0, 26100);
        Assert.Equal(2, comparison.InfExamined);
        Assert.Equal(1, comparison.RejectedInfCount);
        Assert.Equal(1, comparison.DevicesWithEvidence);
        // The genuine Realtek INF is recognized as device evidence,
        // but the local preflight does not assert a newer compatible
        // package without the rest of its checks. The corrupt NVIDIA
        // INF must not contribute any evidence or newer-driver claim.
        Assert.Equal(0, comparison.PotentialNewerDevices);
        Assert.False(comparison.IsInstallable);
        Assert.False(comparison.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE_ORDER_991", comparison.Summary);
        Assert.DoesNotContain("PRIVATE_GPU_INSTANCE", comparison.Summary);
    }
}
