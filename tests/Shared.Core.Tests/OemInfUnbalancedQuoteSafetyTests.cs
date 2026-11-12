using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Malformed INF Models directives cannot authorize even read-only
/// "newer OEM driver" hints merely because a valid primary HardwareID
/// appeared BEFORE an unterminated quoted alias.
/// </summary>
public sealed class OemInfUnbalancedQuoteSafetyTests
{
    private const string Nvidia =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";

    private static DriverInfo Device(string id, string instance) =>
        new(instance, "Generic Windows device", "Microsoft",
            "Display", "Microsoft", "31.0.15.6000", null,
            "PRIVATE_oem11.inf", true, true, 0, [id], []);

    private static string Inf(string modelLine) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "DriverVer=11/10/2026,32.0.15.7000\n" +
        "[Manufacturer]\n%Vendor%=Models,NTamd64.10.0...22000\n" +
        "[Models.NTamd64.10.0...22000]\n" + modelLine + "\n" +
        "[Strings]\nModel=\"NVIDIA; GPU\"\n";

    private static MemoryStream Zip(params (string Name, string Text)[] files)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(
            stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(
                    zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    [Theory]
    [InlineData("%Model%=Install,PCI\\VEN_10DE&DEV_28A0&SUBSYS_16A217AA,\"PCI\\VEN_8086&DEV_51F1")]
    [InlineData("%Model%=Install,\"PCI\\VEN_10DE&DEV_28A0&SUBSYS_16A217AA")]
    [InlineData("%Model%=Install,PCI\\VEN_10DE&DEV_28A0&SUBSYS_16A217AA,\"PCI\\VEN_8086&DEV_51F1 ; outside quote?")]
    public void CorruptedModelLineNeverCreatesPositiveNvidiaEvidence(
        string models)
    {
        var text = Inf(models);
        Assert.Throws<InvalidDataException>(() =>
            InfLogicalLines.Read(text).ToArray());
        Assert.Throws<InvalidDataException>(() =>
            InfModelEvidence.ReadDeclaredIds(text));
        Assert.Throws<InvalidDataException>(() =>
            InfModelEvidence.ReadMatchingIds(text,
                [Device(Nvidia, "PRIVATE_GPU")]));
        Assert.Throws<InvalidDataException>(() =>
            InfPlatformPreflight.Assess(text, Device(Nvidia, "PRIVATE_GPU"),
                "x64", 10, 0, 26100, catalogFileExists: false));
    }

    [Fact]
    public void CompletedQuotedCommentEscapesAndMultilineModelsAreStillReadable()
    {
        var valid = Inf("%Model;Name%=Install,\\ ;comment outside token\n" +
                        "\"" + Nvidia + "\"") +
                    "\nName=\"An \"\"RTX\"\"; vendor's display\" ; comment\n" +
                    "Value=one ; \"comment with unmatched quote";
        var lines = InfLogicalLines.Read(valid).ToArray();
        Assert.Contains(lines, line => line.Contains(
            "Name=\"An \"\"RTX\"\"; vendor's display\""));
        Assert.Contains(lines, line => line == "Value=one");
        var evidence = InfModelEvidence.ReadMatchingIds(valid,
            [Device(Nvidia, "PRIVATE_GPU")]);
        Assert.Equal(2, InfModelEvidence.MatchStrength(
            Device(Nvidia, "PRIVATE_GPU"), evidence));
        Assert.Equal(Nvidia, Assert.Single(evidence).HardwareId);
    }

    [Fact]
    public void BrokenInfDoesNotHideHealthyIntelKillerInSameZip()
    {
        var broken = Inf("%Gpu%=Install," + Nvidia + ",\"" + Killer);
        var valid = Inf("%Killer%=Install," + Killer);
        using var zip = Zip(
            ("PRIVATE_ORDER_214/broken.inf", broken),
            ("intel/killer.inf", valid));

        var preview = OemZipInfEvidence.Inspect(zip);
        Assert.Equal(2, preview.InfFiles.Count);
        Assert.Equal(1, preview.RejectedInfCount);
        Assert.Equal(1, preview.InfWithDeclaredIds);
        Assert.DoesNotContain("PRIVATE_ORDER_214",
            string.Join(" ", preview.Warnings));

        zip.Position = 0;
        var comparison = LocalOemDriverComparison.Compare(zip,
        [
            Device(Nvidia, "PRIVATE_GPU_1"),
            Device(Killer, "PRIVATE_KILLER_2")
        ], "x64", 10, 0, 26100);

        Assert.Equal(2, comparison.InfExamined);
        Assert.Equal(1, comparison.RejectedInfCount);
        Assert.Equal(1, comparison.InfWithDeclaredIds);
        var row = Assert.Single(comparison.Findings);
        Assert.Equal("intel/killer.inf", row.InfName);
        Assert.True(row.HasExactHardwareId);
        Assert.Equal(1, comparison.PotentialNewerDevices);
        Assert.Contains(comparison.Notes, note =>
            note.Contains("parziale", StringComparison.OrdinalIgnoreCase));
        Assert.False(comparison.IsInstallable);
        Assert.False(comparison.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE", comparison.Summary);
        Assert.DoesNotContain(Nvidia, comparison.Summary);
    }

    [Fact]
    public void OnlyMalformedQuotedInfIsPartialNotVerifiedZeroOffers()
    {
        using var zip = Zip(("vendor/broken.inf",
            Inf("%Gpu%=Install," + Nvidia + ",\"" + Killer)));
        var report = LocalOemDriverComparison.Compare(zip,
            [Device(Nvidia, "PRIVATE_GPU")],
            "AMD64", 10, 0, 26100);
        Assert.Equal(1, report.InfExamined);
        Assert.Equal(1, report.RejectedInfCount);
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.Contains(report.Notes, note =>
            note.Contains("non una prova di assenza"));
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void CorruptAndHealthyInfInExtractedFolderRemainIndependent()
    {
        var folder = Directory.CreateTempSubdirectory(
            "OmegaDrive-InfQuoteSafety-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "bad.inf"),
                Inf("%Gpu%=Install," + Nvidia + ",\"" + Killer));
            File.WriteAllText(Path.Combine(folder.FullName, "good.inf"),
                Inf("%Wifi%=Install," + Killer));
            var report = LocalOemFolderComparison.Compare(
                folder.FullName,
                [
                    Device(Nvidia, "PRIVATE_GPU_1"),
                    Device(Killer, "PRIVATE_WIFI_2")
                ], "x64", 10, 0, 26100);
            Assert.Equal(2, report.InfExamined);
            Assert.Equal(1, report.RejectedInfCount);
            Assert.Single(report.Findings);
            Assert.Equal(1, report.PotentialNewerDevices);
            Assert.False(report.IsInstallable);
            Assert.DoesNotContain("PRIVATE_GPU_1", report.Summary);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void OverlongOrUnfinishedContinuedDirectiveRetainsSafetyCaps()
    {
        Assert.Throws<InvalidDataException>(() =>
            InfLogicalLines.Read("Value=\"" + new string('X',
                InfLogicalLines.MaxLogicalLineCharacters + 1))
                .ToArray());
        Assert.Throws<InvalidDataException>(() =>
            InfLogicalLines.Read("Value=\"Unfinished\\\n").ToArray());
        Assert.Equal(["Value=\"Closed\\\""],
            InfLogicalLines.Read("Value=\"Closed\\\"").ToArray());
    }
}
