using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>End-to-end, read-only OEM evidence for real Windows PnP bus
/// formats previously rejected by InfModelEvidence before comparison.</summary>
public sealed class OemModernBusCoverageTests
{
    private const string IntelAudioId =
        @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295&SUBSYS_12345678";
    private const string NvidiaSoftwareId =
        @"SWC\VEN_10DE&DEV_00F4";

    private static DriverInfo Device(string id, string name, string deviceClass,
        string version) =>
        new("PRIVATE_DEVICE_INSTANCE_123", name, "Microsoft", deviceClass,
            "Microsoft", version, null, "oem123.inf", true,
            true, 0, [id], []);

    private static string Inf(string id, string deviceClass, string version) =>
        $"""
        [Version]
        Signature="$Windows NT$"
        Class={deviceClass}
        CatalogFile.NTamd64=unchecked.cat
        DriverVer=10/04/2026,{version}
        [Manufacturer]
        %Vendor%=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        %Device%=Install,{id}
        """;

    private static MemoryStream Zip(params (string Path, string Contents)[] entries)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, contents) in entries)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(contents);
            }
        }
        buffer.Position = 0;
        return buffer;
    }

    [Theory]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295")]
    [InlineData(@"intelaudio\func_01&ven_10ec&dev_0295")]
    [InlineData(@"SWC\VEN_10DE&DEV_00F4")]
    [InlineData(@"SWC\VID0001&PID0001&SID0001")]
    public void ReadOnlyInfParserRetainsOfficialWindowsModelBusIds(string id)
    {
        var declared = Assert.Single(InfModelEvidence.ReadDeclaredIds(
            Inf(id, "SoftwareComponent", "1.2.3.4")));
        Assert.Equal(id.ToUpperInvariant(), declared.HardwareId);
        Assert.Equal("Models.NTamd64.10.0...22000", declared.Section);
    }

    [Theory]
    [InlineData(@"SWC\..\evil")]
    [InlineData(@"SWC\VEN_10DE&DEV_00F4/evil")]
    [InlineData(@"SWC\VEN_10DE&DEV_00F4\PRIVATE")]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC;SWC\VEN_10DE")]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC with spaces")]
    [InlineData(@"FAKEBUS\VEN_10DE&DEV_00F4")]
    public void RejectsPathLikeMultipleAndUnknownBusIdentifiers(string id) =>
        Assert.Empty(InfModelEvidence.ReadDeclaredIds(
            Inf(id, "SoftwareComponent", "1.2.3.4")));

    [Fact]
    public void ZipCanCorrelateRealtekIntelSmartSoundAndNvidiaSoftwareComponent()
    {
        using var zip = Zip(
            ("realtek/audio.inf", Inf(IntelAudioId, "MEDIA", "6.0.10013.1")),
            ("nvidia/component.inf", Inf(NvidiaSoftwareId,
                "SoftwareComponent", "32.0.16.2000")));

        var installed = new[]
        {
            Device(IntelAudioId, "Generic Multimedia Device", "MEDIA",
                "6.0.9600.1"),
            Device(NvidiaSoftwareId, "Generic Software Component",
                "SoftwareComponent", "31.0.15.6000")
        };

        var report = LocalOemDriverComparison.Compare(zip,
            installed, "AMD64", 10, 0, 26100);

        Assert.Equal(2, report.InfExamined);
        Assert.Equal(2, report.InfWithDeclaredIds);
        Assert.Equal(2, report.Findings.Count);
        Assert.Equal(2, report.DevicesWithEvidence);
        Assert.Equal(2, report.PotentialNewerDevices);
        Assert.All(report.Findings, finding =>
        {
            Assert.True(finding.HasExactHardwareId);
            Assert.Equal(2, finding.HardwareMatchStrength);
            Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
                finding.PlatformVerdict);
            Assert.Equal(LocalOemDriverComparison.VersionRelation.NewerHint,
                finding.VersionComparison);
        });

        // These are INF hints from a LOCAL, USER-PROVIDED file. Not a source
        // of Windows Update packages, WHQL signatures, OEM applicability or
        // installation permission.
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.Contains("NON certifica firma", report.Summary);
        Assert.DoesNotContain("PRIVATE_DEVICE_INSTANCE", report.Summary);
    }

    [Fact]
    public void ExtractedOemFolderSharesTheSameNewInfBusCoverage()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-SwcAudioInf-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "audio"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "software"));
            File.WriteAllText(Path.Combine(root.FullName, "audio", "realtek.inf"),
                Inf(IntelAudioId, "MEDIA", "6.0.10013.1"));
            File.WriteAllText(Path.Combine(root.FullName, "software", "component.inf"),
                Inf(NvidiaSoftwareId, "SoftwareComponent", "32.0.16.2000"));

            var report = LocalOemFolderComparison.Compare(root.FullName,
            [
                Device(IntelAudioId, "Generic Audio", "MEDIA", "6.0.9600.1"),
                Device(NvidiaSoftwareId, "Generic Component",
                    "SoftwareComponent", "31.0.15.6000")
            ], "x64", 10, 0, 26100);

            Assert.Equal(2, report.InfExamined);
            Assert.Equal(2, report.DevicesWithEvidence);
            Assert.False(report.IsInstallable);
            Assert.False(report.IsVerifiedCompatible);
            Assert.Contains("Nessun EXE", string.Join(" ", report.Notes));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void GenericNvidiaSwcIsRecognizedOnlyForExactDelimitedVendorToken()
    {
        var nvidia = Device(NvidiaSoftwareId, "Generic Software Component",
            "SoftwareComponent", "31.0.15.6000");
        Assert.True(NvidiaHardwareDetector.IsNvidiaDevice(nvidia));
        var handoff = Assert.Single(VendorSourceDiscovery.Find([nvidia], []));
        Assert.Equal("NVIDIA", handoff.Vendor);
        Assert.Equal(0, handoff.WuaMatches);
        Assert.False(handoff.IsVendorApplicable);
        Assert.False(handoff.IsCatalogMetadata);

        var evidence = Assert.Single(VendorDriverEvidenceFinder.Find([nvidia], []));
        Assert.Equal("NVIDIA", evidence.Vendor);
        Assert.Equal(0, evidence.CorrelatedWuaCandidates);
        Assert.Contains("stato OEM non verificato", evidence.DisplayLine);

        var unrelated = nvidia with
        {
            HardwareIds = [@"SWC\VEN_10DE0&DEV_00F4"],
            CompatibleIds = []
        };
        Assert.False(NvidiaHardwareDetector.IsNvidiaDevice(unrelated));
        Assert.Empty(VendorSourceDiscovery.Find([unrelated], []));

        var ambiguous = nvidia with
        {
            HardwareIds = [@"SWC\VEN_10DE"],
            CompatibleIds = []
        };
        Assert.False(NvidiaHardwareDetector.IsNvidiaDevice(ambiguous));
    }

    [Fact]
    public void GenericRealtekSstHandoffDoesNotFabricateNewUpdates()
    {
        var audio = Device(IntelAudioId, "Generic Multimedia Device", "MEDIA",
            "6.0.9600.1");

        var handoff = Assert.Single(VendorSourceDiscovery.Find([audio], []));
        Assert.Equal("Realtek", handoff.Vendor);
        Assert.False(handoff.IsVendorApplicable);
        Assert.Equal(0, handoff.WuaMatches);

        var hint = HardwareIdentityParser.Describe(NvidiaSoftwareId);
        Assert.Contains("componente software PnP", hint);
        Assert.Contains("senza dedurne il produttore", hint);
        Assert.DoesNotContain("NVIDIA", hint);
    }

    [Fact]
    public void CompatibleIdOnlyStillCannotConfirmNewerApplicableOemDriver()
    {
        var nvidia = Device(@"SWC\VEN_9999&DEV_0001",
            "Generic Software Component", "SoftwareComponent",
            "31.0.15.6000") with
        {
            CompatibleIds = [NvidiaSoftwareId]
        };
        using var zip = Zip(("nvidia/component.inf",
            Inf(NvidiaSoftwareId, "SoftwareComponent", "32.0.16.2000")));
        var report = LocalOemDriverComparison.Compare(zip,
            [nvidia], "AMD64", 10, 0, 26100);

        var found = Assert.Single(report.Findings);
        Assert.Equal(1, found.HardwareMatchStrength);
        Assert.False(found.HasExactHardwareId);
        Assert.False(found.IsPotentialNewer);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.False(report.IsInstallable);
    }
}
