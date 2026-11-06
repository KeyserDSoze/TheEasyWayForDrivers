using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class ModernAudioInfCoverageTests
{
    private const string SoundWire =
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711&TYPE_04&VER_01&ADR_01&LID_00&UID_00&SUBSYS_305610EC";
    private const string Sndw =
        @"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00";
    private const string Acp =
        @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2";

    private static DriverInfo Device(string instanceId, string hardwareId) =>
        new(instanceId, "Generic Windows audio device", "Microsoft",
            "MEDIA", "Microsoft", "1.0.0.0", null, "oem-test.inf",
            true, true, 0, [hardwareId], []);

    private static string Inf(string primary, string? compatible = null) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "DriverVer=10/04/2026,2.0.0.0\n" +
        "[Manufacturer]\n%Vendor%=Models,NTamd64.10.0...22000\n" +
        "[Models.NTamd64.10.0...22000]\n" +
        "%Device%=Install," + primary +
        (compatible is null ? "" : "," + compatible) + "\n";

    [Theory]
    [InlineData(SoundWire)]
    [InlineData(Sndw)]
    [InlineData(Acp)]
    public void WholeInfAndTargetedComparisonRecognizeModernAudioBuses(string hardwareId)
    {
        var device = Device("PRIVATE_INSTANCE", hardwareId);
        var inf = Inf(hardwareId);

        Assert.Equal(2, InfModelEvidence.MatchStrength(
            device, InfModelEvidence.ReadDeclaredIds(inf)));
        var target = Assert.Single(InfModelEvidence.ReadMatchingIds(inf, [device]));
        Assert.True(target.IsPrimaryHardwareId);
        Assert.Equal(hardwareId.ToUpperInvariant(), target.HardwareId);

        var platform = InfPlatformPreflight.Assess(
            inf, device, "x64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            platform.Result);
        Assert.Equal(2, platform.HardwareMatchStrength);
        Assert.Contains("non verificata", platform.CatalogStatus.ToLowerInvariant());
    }

    [Fact]
    public void CompatibleAliasRemainsWeakerAndDifferentBusIdsNeverMatch()
    {
        var inf = Inf(@"SOUNDWIRE\AGGREGATOR", SoundWire);
        var declared = InfModelEvidence.ReadDeclaredIds(inf);
        Assert.Equal(1, InfModelEvidence.MatchStrength(
            Device("PRIVATE_SDW", SoundWire), declared));
        Assert.Equal(0, InfModelEvidence.MatchStrength(
            Device("PRIVATE_NEAR_MISS", SoundWire + "0"), declared));
        Assert.Equal(0, InfModelEvidence.MatchStrength(
            Device("PRIVATE_DIFFERENT_BUS", SoundWire.Replace("SOUNDWIRE", "SNDW")), declared));

        var platform = InfPlatformPreflight.Assess(
            inf, Device("PRIVATE_SDW", SoundWire), "x64", 10, 0, 26100, false);
        Assert.Equal(1, platform.HardwareMatchStrength);
    }

    [Fact]
    public void BothZipAndExtractedFolderFindModernAudioInfWithoutInstalling()
    {
        var devices = new[]
        {
            Device("PRIVATE_SDW", SoundWire),
            Device("PRIVATE_ACP", Acp)
        };
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, id) in new[]
            {
                ("soundwire.inf", SoundWire),
                ("acp.inf", Acp)
            })
            {
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(Inf(id));
            }
        }
        stream.Position = 0;
        var zipResult = LocalOemDriverComparison.Compare(
            stream, devices, "x64", 10, 0, 26100);
        Assert.Equal(2, zipResult.Findings.Count);
        Assert.All(zipResult.Findings, row => Assert.True(row.HasExactHardwareId));
        Assert.False(zipResult.IsInstallable);
        Assert.False(zipResult.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE_", zipResult.Summary);

        var folder = Directory.CreateTempSubdirectory("OmegaDrive-ModernAudio-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "soundwire.inf"), Inf(SoundWire));
            File.WriteAllText(Path.Combine(folder.FullName, "acp.inf"), Inf(Acp));
            var folderResult = LocalOemFolderComparison.Compare(
                folder.FullName, devices, "x64", 10, 0, 26100);
            Assert.Equal(2, folderResult.Findings.Count);
            Assert.False(folderResult.IsInstallable);
            Assert.False(folderResult.IsVerifiedCompatible);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(@"SOUNDWIRE\..\evil.inf")]
    [InlineData(@"SNDW\CLASS_00/evil")]
    [InlineData(@"ACP\DEVTYPE_0005 with spaces")]
    [InlineData(@"SOUNDWIRE0\AGGREGATOR")]
    public void NewBusTokensDoNotWeakenInfIdValidation(string id) =>
        Assert.Empty(InfModelEvidence.ReadDeclaredIds(Inf(id)));
}
