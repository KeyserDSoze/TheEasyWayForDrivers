using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Windows INF Models: first ID is primary; subsequent IDs are compatible.
/// Physical lines can be continued with a trailing backslash.
/// </summary>
public sealed class InfLogicalLinesAndCompatibleIdsTests
{
    private const string NvidiaGpu =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string KillerWifi =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_11118086";
    private const string RealtekAudio =
        @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(string id, string version = "31.0.15.6000",
        string deviceClass = "Display") =>
        new("LOCAL_PRIVATE_INSTANCE", "Generic Windows device", "Microsoft",
            deviceClass, "Microsoft", version, null, "oem9.inf", true, true, 0,
            [id], []);

    private static string Inf(string primaryId, string compatibleId) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "DriverVer=10/04/2026,32.0.16.2000\n" +
        "[Manufacturer]\n%Vendor%=Models,NTamd64.10.0...22000,\\\n" +
        "NTarm64.10.0...22000\n" +
        "[Models.NTamd64.10.0...22000]\n" +
        "%Model%=Install,\\ ; physical-line comment\n" +
        primaryId + ",\\\n" +
        compatibleId + "\n" +
        "[Models.NTarm64.10.0...22000]\n" +
        "%Other%=Install,PCI\\VEN_9999&DEV_9999\n";

    private static MemoryStream Zip(string name, string text)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void JoinsMultilineManufacturerAndModelHardwareIdsWithoutDroppingComments()
    {
        var inf = Inf(NvidiaGpu, KillerWifi);
        var declared = InfModelEvidence.ReadDeclaredIds(inf);

        Assert.Contains(declared, row =>
            row.Section == "Models.NTamd64.10.0...22000" &&
            row.HardwareId == NvidiaGpu && row.IsPrimaryHardwareId);
        Assert.Contains(declared, row =>
            row.Section == "Models.NTamd64.10.0...22000" &&
            row.HardwareId == KillerWifi && !row.IsPrimaryHardwareId);

        Assert.Equal(2, InfModelEvidence.MatchStrength(
            Device(NvidiaGpu), declared));
        Assert.Equal(1, InfModelEvidence.MatchStrength(
            Device(KillerWifi), declared));
        Assert.Equal(2, InfPlatformPreflight.Assess(
            inf, Device(NvidiaGpu), "x64", 10, 0, 26100, false)
            .HardwareMatchStrength);
        Assert.Equal(1, InfPlatformPreflight.Assess(
            inf, Device(KillerWifi), "AMD64", 10, 0, 26100, false)
            .HardwareMatchStrength);
    }

    [Fact]
    public void InfCompatibleAliasMatchingWindowsHardwareIdCannotClaimNewerDriver()
    {
        using var zip = Zip("official/recoil.inf", Inf(NvidiaGpu, KillerWifi));
        var report = LocalOemDriverComparison.Compare(zip,
            [Device(KillerWifi, "23.0.1.1", "Net")],
            "x64", 10, 0, 26100);

        var row = Assert.Single(report.Findings);
        Assert.Equal(1, row.HardwareMatchStrength);
        Assert.False(row.HasExactHardwareId);
        Assert.False(row.IsPotentialNewer);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.Contains("Compatible ID", row.Summary);
        Assert.DoesNotContain("LOCAL_PRIVATE_INSTANCE", report.Summary);
    }

    [Fact]
    public void MatchingPrimaryNvidiaHardwareIdInMultilineZipRetainsNewerHint()
    {
        using var zip = Zip("official/gpu.inf", Inf(NvidiaGpu, KillerWifi));
        var report = LocalOemDriverComparison.Compare(zip,
            [Device(NvidiaGpu)], "x64", 10, 0, 26100);
        var row = Assert.Single(report.Findings);
        Assert.Equal(2, row.HardwareMatchStrength);
        Assert.True(row.HasExactHardwareId);
        Assert.True(row.IsPotentialNewer);
        Assert.Equal(1, report.PotentialNewerDevices);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
    }

    [Fact]
    public void ExtractedOemFolderUsesSameLogicalLineReaderAndStrength()
    {
        var folder = Directory.CreateTempSubdirectory("OmegaDrive-InfContinuation-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "nvidia.inf"),
                Inf(NvidiaGpu, KillerWifi));
            File.WriteAllText(Path.Combine(folder.FullName, "realtek.inf"),
                Inf(RealtekAudio, @"INTELAUDIO\FUNC_01&VEN_10EC"));

            var report = LocalOemFolderComparison.Compare(folder.FullName,
            [
                Device(NvidiaGpu),
                Device(RealtekAudio, "6.0.9600.1", "MEDIA") with
                {
                    DeviceId = "LOCAL_PRIVATE_AUDIO",
                    Name = "Generic Windows Audio"
                }
            ], "AMD64", 10, 0, 26100);

            Assert.Equal(2, report.InfExamined);
            Assert.Equal(2, report.Findings.Count);
            Assert.Equal(2, report.PotentialNewerDevices);
            Assert.False(report.IsInstallable);
            Assert.False(report.IsVerifiedCompatible);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void ApplicableCompatibleIdCannotInheritPrimaryStrengthFromOtherArchitecture()
    {
        var inf = """
            [Version]
            Signature="$Windows NT$"
            [Manufacturer]
            %Vendor%=Models,NTamd64,NTarm64
            [Models.NTamd64]
            %Local%=Install,PCI\VEN_9999&DEV_0001,PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA
            [Models.NTarm64]
            %Other%=Install,PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA
            """;
        var result = InfPlatformPreflight.Assess(inf,
            Device(NvidiaGpu), "x64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            result.Result);
        Assert.Equal(1, result.HardwareMatchStrength);
        Assert.Contains("Compatible ID", result.Explanation);
    }

    [Fact]
    public void PrimaryIdStillWinsWhenItAppearsAfterSameCompatibleAlias()
    {
        var inf = """
            [Manufacturer]
            %Vendor%=Models,NTamd64
            [Models.NTamd64]
            %First%=Install,PCI\VEN_9999&DEV_0001,PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA
            %Second%=Install,PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA
            """;
        var declared = InfModelEvidence.ReadDeclaredIds(inf)
            .Where(item => item.HardwareId == NvidiaGpu).ToArray();
        Assert.Equal(2, declared.Length);
        Assert.Contains(declared, item => item.IsPrimaryHardwareId);
        Assert.Contains(declared, item => !item.IsPrimaryHardwareId);
        Assert.Equal(2, InfModelEvidence.MatchStrength(
            Device(NvidiaGpu), declared));
    }

    [Fact]
    public void SemicolonInsideQuotedStringAndStringTokenIsNotAComment()
    {
        var lines = InfLogicalLines.Read("""
            [Strings]
            Title="NVIDIA; RTX device"
            %Some;Key%=A;comment outside token
            """).ToArray();

        Assert.Contains("""Title="NVIDIA; RTX device" """.Trim(), lines);
        Assert.Contains("%Some;Key%=A", lines);
    }

    [Fact]
    public void QuotedBackslashDoesNotContinueIntoNextInfDirective()
    {
        var lines = InfLogicalLines.Read("""
            [Manufacturer]
            %Vendor%=Models,NTamd64
            [SourceDisksNames]
            1="SomeDirectory\"
            [Models.NTamd64]
            %Model%=Install,PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA
            """).ToArray();

        Assert.Contains("1=\"SomeDirectory\\\"", lines);
        Assert.Contains("[Models.NTamd64]", lines);
        Assert.Contains(lines, value => value.StartsWith("%Model%"));
    }

    [Theory]
    [InlineData("Model=Install,PCI\\VEN_10DE&DEV_28A0\\")]
    [InlineData("%Vendor%=Models,NTamd64,\\ ;comment")]
    public void TruncatedContinuedInfDirectiveIsRejected(string incomplete) =>
        Assert.Throws<InvalidDataException>(() =>
            InfLogicalLines.Read(incomplete).ToArray());

    [Fact]
    public void TooManyContinuationsOrOversizedLogicalLineAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            InfLogicalLines.Read("Key=" +
                string.Concat(Enumerable.Repeat("\\\n", 65)) + "Data")
                .ToArray());

        Assert.Throws<InvalidDataException>(() =>
            InfLogicalLines.Read("Key=" + new string('A', 32769))
                .ToArray());
        Assert.Throws<InvalidDataException>(() =>
            InfLogicalLines.Read(new string('A', 1024 * 1024 + 1))
                .ToArray());
    }

    [Fact]
    public void UnreferencedModelSectionAndSimilarVendorIdsStayExcluded()
    {
        var inf = """
            [Manufacturer]
            %Vendor%=Models,NTamd64
            [Models.NTarm64]
            %WrongArch%=Install,PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA
            [Models.NTamd64]
            %NearMiss%=Install,PCI\VEN_10DE0&DEV_28A0&SUBSYS_16A217AA
            """;
        var result = InfPlatformPreflight.Assess(inf,
            Device(NvidiaGpu), "x64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ManualReviewRequired,
            result.Result); // missing Signature, never a positive preflight
        Assert.Empty(InfModelEvidence.ReadMatchingIds(inf,
            [Device(NvidiaGpu)]));
    }
}
