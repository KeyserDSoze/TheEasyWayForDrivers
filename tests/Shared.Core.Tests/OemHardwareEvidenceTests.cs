using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemHardwareEvidenceTests
{
    private static DriverInfo Device() => new("id", "Test device", "Intel",
        "System", "Intel", "1.0", null, null, true, true, 0,
        [@"PCI\VEN_8086&DEV_1234&SUBSYS_00000001"],
        [@"PCI\VEN_8086&DEV_1234"]);

    private static OemZipInfEvidence.Report Evidence(params string[] ids) =>
        new([new OemZipInfEvidence.InfEntry("drivers/a.inf",
            ids.Select(id => new InfModelEvidence.DeclaredId("Models.NTamd64", id)).ToArray(),
            "Dichiarazioni INF")], []);

    [Fact]
    public void PrefersExactHardwareIdOverCompatibleId()
    {
        var result = OemHardwareEvidence.Compare(Evidence(
            @"PCI\VEN_8086&DEV_1234&SUBSYS_00000001",
            @"PCI\VEN_8086&DEV_1234"), [Device()]);
        Assert.Equal(2, result.Matches.Count);
        Assert.Equal(OemHardwareEvidence.Strength.HardwareId, result.Matches[0].Level);
        Assert.Equal(OemHardwareEvidence.Strength.CompatibleId, result.Matches[1].Level);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerifiedCompatible);
    }

    [Fact]
    public void SubstringOrDifferentIdentifierNeverMatches()
    {
        var result = OemHardwareEvidence.Compare(
            Evidence(@"PCI\VEN_8086&DEV_123", @"PCI\VEN_8086&DEV_1235"), [Device()]);
        Assert.Empty(result.Matches);
    }

    private static DriverInfo DeviceWithId(string name, string id) =>
        new("PRIVATE_INSTANCE_" + name, name, "Microsoft", "Unknown",
            "Microsoft", "1.0", null, "oem0.inf", true, true, 0,
            [id], []);

    private static MemoryStream Zip(params (string Name, string Text)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(
                    entry.Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static string Inf(string models) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\nVendor=Models,NTamd64\n" +
        "[Models.NTamd64]\n" + models + "\n";

    [Fact]
    public void StaticInspectorFindsNvidiaKillerAndRealtekAfterThe500IdPreview()
    {
        const string nvidia =
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
        const string killer = @"PCI\VEN_8086&DEV_51F1";
        const string realtek =
            @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
        var unrelated = string.Join("\n", Enumerable.Range(0, 560)
            .Select(i => $"Other{i}=Install,PCI\\VEN_9999&DEV_{i:X4}"));
        var models = unrelated + $"\nRTX=Install,{nvidia}\n" +
            $"Killer=Install,{killer}\nRealtek=Install,{realtek}";
        using var archive = Zip(
            (@"OEM\Drivers\NVIDIA.inf", Inf(models)));
        var devices = new[]
        {
            DeviceWithId("NVIDIA GPU", nvidia),
            DeviceWithId("Intel Killer", killer),
            DeviceWithId("Realtek Audio", realtek)
        };

        var oldPreview = OemZipInfEvidence.Inspect(archive);
        Assert.Equal(500, oldPreview.DeclaredIdCount);
        Assert.Empty(OemHardwareEvidence.Compare(oldPreview, devices).Matches);

        archive.Position = 0;
        var evidence = OemZipInfEvidence.Inspect(archive, devices);
        Assert.True(evidence.HasTargetedDeviceMatches);
        Assert.Equal(500, evidence.DeclaredIdCount);
        var targeted = Assert.Single(evidence.TargetedMatches);
        Assert.Equal("OEM/Drivers/NVIDIA.inf", targeted.Key);
        Assert.Equal(3, targeted.Value.Count);
        var report = OemHardwareEvidence.Compare(evidence, devices);
        Assert.True(report.UsedFullTargetedMatching);
        Assert.Equal(3, report.Matches.Count);
        Assert.All(report.Matches, match =>
            Assert.Equal(OemHardwareEvidence.Strength.HardwareId, match.Level));
        Assert.Contains(report.Matches, match => match.DeviceName == "NVIDIA GPU");
        Assert.Contains(report.Matches, match => match.DeviceName == "Intel Killer");
        Assert.Contains(report.Matches, match => match.DeviceName == "Realtek Audio");
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.False(report.HasUnexaminedInf);
    }

    [Fact]
    public void SecondaryInfCompatibleAliasNeverClaimsExactPnpHardwareId()
    {
        const string primary =
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_11111111";
        const string alias =
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
        var device = DeviceWithId("NVIDIA Laptop GPU", alias);
        using var archive = Zip(("display.inf",
            Inf($"RTX=Install,{primary},{alias}")));

        var evidence = OemZipInfEvidence.Inspect(archive, [device]);
        var report = OemHardwareEvidence.Compare(evidence, [device]);
        var result = Assert.Single(report.Matches);
        Assert.Equal(alias, result.DeclaredId);
        Assert.Equal(OemHardwareEvidence.Strength.CompatibleId, result.Level);
        Assert.True(report.UsedFullTargetedMatching);
        Assert.False(report.IsInstallable);

        // Preview-only callers still must not elevate the alias to exact.
        archive.Position = 0;
        var preview = OemZipInfEvidence.Inspect(archive);
        Assert.Equal(OemHardwareEvidence.Strength.CompatibleId,
            Assert.Single(OemHardwareEvidence.Compare(preview, [device]).Matches).Level);
    }

    [Fact]
    public void MalformedInfIsNotMatchedEvenIfValidIdsPrecedeTheBadDirective()
    {
        const string nvidia = @"PCI\VEN_10DE&DEV_28A0";
        const string killer = @"PCI\VEN_8086&DEV_51F1";
        var malformed = Inf($"RTX=Install,{nvidia}") +
            "[Broken]\nBad=Value,\\" +
            string.Concat(Enumerable.Repeat("\nmore,\\", 65)) +
            "\nend\n";
        using var archive = Zip(
            ("nvidia/broken.inf", malformed),
            (@"killer\valid.inf", Inf($"WiFi=Install,{killer}")));
        var devices = new[]
        {
            DeviceWithId("NVIDIA GPU", nvidia),
            DeviceWithId("Killer WiFi", killer)
        };

        var evidence = OemZipInfEvidence.Inspect(archive, devices);
        Assert.Equal(1, evidence.RejectedInfCount);
        Assert.True(evidence.HasTargetedDeviceMatches);
        Assert.DoesNotContain("nvidia/broken.inf", evidence.TargetedMatches.Keys);
        Assert.Contains("killer/valid.inf", evidence.TargetedMatches.Keys);
        var result = OemHardwareEvidence.Compare(evidence, devices);
        Assert.True(result.HasUnexaminedInf);
        Assert.Single(result.Matches);
        Assert.Equal("Killer WiFi", result.Matches[0].DeviceName);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerifiedCompatible);
    }

    [Fact]
    public void EmptyEvidenceAndDeviceInventoryAreSafe()
    {
        Assert.Empty(OemHardwareEvidence.Compare(Evidence(), [Device()]).Matches);
        Assert.Empty(OemHardwareEvidence.Compare(
            Evidence(@"PCI\VEN_8086&DEV_1234"), []).Matches);
    }
}
