using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// ZIP Inspector preview and full targeted ZIP matching must agree that
/// PCI class/vendor-only tokens are NOT attributable device models.
/// This blocks false matches for GPUs, network adapters and WUA-style
/// weak compatible IDs without losing genuine NVIDIA/Killer/Realtek IDs.
/// </summary>
public sealed class OemZipPciClassMatchRegressionTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Audio = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string GpuClass = @"PCI\CC_030000";
    private const string NetworkClass = @"PCI\CC_0280";

    private static DriverInfo Device(
        string hardware, string compatible, string name = "Generic device") =>
        new("PRIVATE_INSTANCE_SERIAL_123", name, "Microsoft", "Unknown",
            "Microsoft", "1.0.0.0", null, null, true, true, 0,
            [hardware], [compatible]);

    private static string Inf(string mainId, string? alias = null) => $"""
        [Version]
        Signature="$Windows NT$"
        Class=Display
        [Manufacturer]
        %Vendor%=Models,NTamd64
        [Models.NTamd64]
        %Model%=Install,{mainId}{(alias is null ? "" : "," + alias)}
        """;

    private static MemoryStream Zip(params (string File, string Contents)[] files)
    {
        var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory,
            ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in files)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(),
                    new UTF8Encoding(false));
                writer.Write(content);
            }

        memory.Position = 0;
        return memory;
    }

    [Theory]
    [InlineData(GpuClass)]
    [InlineData(NetworkClass)]
    [InlineData(@"PCI\VEN_10DE")]
    [InlineData(@"PCI\VEN_10DE&CC_0300")]
    [InlineData(@"PCI\VEN_8086&CC_0280")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&FAKE_0100")]
    public void GenericPciMetadataNeverCreatesPreviewOrTargetedDeviceMatch(
        string id)
    {
        using var zip = Zip(("generic.inf", Inf(id)));
        var device = Device(id, id);

        var preview = OemZipInfEvidence.Inspect(zip);
        Assert.Equal(id.ToUpperInvariant(),
            Assert.Single(Assert.Single(preview.InfFiles).DeclaredIds).HardwareId);
        Assert.Empty(OemHardwareEvidence.Compare(preview, [device]).Matches);

        zip.Position = 0;
        var targeted = OemZipInfEvidence.Inspect(zip, [device]);
        Assert.True(targeted.HasTargetedDeviceMatches);
        Assert.Empty(OemHardwareEvidence.Compare(targeted, [device]).Matches);
        Assert.False(targeted.IsInstallable);
        Assert.False(targeted.IsVerified);
    }

    [Fact]
    public void MixedInfPreservesRealNvidiaKillerAndRealtekButRejectsClassAliases()
    {
        var gpu = Device(Gpu, GpuClass, "NVIDIA GPU");
        var wifi = Device(Killer, NetworkClass, "Killer Wi-Fi");
        var audio = Device(Audio, @"HDAUDIO\FUNC_01", "Realtek Audio");
        using var zip = Zip(
            ("00-gpu-class.inf", Inf(GpuClass)),
            ("01-net-class.inf", Inf(NetworkClass)),
            ("02-gpu-model.inf", Inf(Gpu, GpuClass)),
            ("03-killer-model.inf", Inf(Killer, NetworkClass)),
            ("04-audio-model.inf", Inf(Audio)));

        foreach (var targeted in new[] { false, true })
        {
            zip.Position = 0;
            var evidence = targeted
                ? OemZipInfEvidence.Inspect(zip, [gpu, wifi, audio])
                : OemZipInfEvidence.Inspect(zip);
            Assert.Equal(5, evidence.InfFiles.Count);
            Assert.Contains(evidence.InfFiles,
                inf => inf.DeclaredIds.Any(id => id.HardwareId == GpuClass));
            var result = OemHardwareEvidence.Compare(
                evidence, [gpu, wifi, audio]);
            Assert.Equal(3, result.Matches.Count);
            Assert.Contains(result.Matches, row =>
                row.DeclaredId == Gpu &&
                row.Level == OemHardwareEvidence.Strength.HardwareId);
            Assert.Contains(result.Matches, row =>
                row.DeclaredId == Killer &&
                row.Level == OemHardwareEvidence.Strength.HardwareId);
            Assert.Contains(result.Matches, row =>
                row.DeclaredId == Audio &&
                row.Level == OemHardwareEvidence.Strength.HardwareId);
            Assert.DoesNotContain(result.Matches,
                row => row.DeclaredId == GpuClass ||
                       row.DeclaredId == NetworkClass);
            Assert.False(result.IsInstallable);
            Assert.False(result.IsVerifiedCompatible);
            Assert.DoesNotContain("PRIVATE_INSTANCE", string.Join(" ",
                result.Matches.Select(match => match.DeviceName)));
        }
    }

    [Fact]
    public void SharedPciClassCannotAssociateWrongGpuModelOrOem()
    {
        var amd = Device(
            @"PCI\VEN_1002&DEV_164E&SUBSYS_00001002",
            GpuClass, "AMD GPU");
        using var zip = Zip(
            ("nvidia.inf", Inf(Gpu, GpuClass)));
        var preview = OemZipInfEvidence.Inspect(zip);

        Assert.Equal(2, Assert.Single(preview.InfFiles).DeclaredIds.Count);
        Assert.Empty(OemHardwareEvidence.Compare(preview, [amd]).Matches);
        zip.Position = 0;
        Assert.Empty(OemHardwareEvidence.Compare(
            OemZipInfEvidence.Inspect(zip, [amd]), [amd]).Matches);
    }

    [Fact]
    public void AnInfCompatibleAliasToRealPnpIdIsWeakerThanPrimaryId()
    {
        var device = Device(Gpu, @"PCI\VEN_10DE&DEV_28A0");
        using var zip = Zip(
            ("gpu.inf", Inf(
                @"PCI\VEN_10DE&DEV_28A0&SUBSYS_00000000", Gpu)));
        var preview = OemZipInfEvidence.Inspect(zip);
        var hit = Assert.Single(
            OemHardwareEvidence.Compare(preview, [device]).Matches);
        Assert.Equal(OemHardwareEvidence.Strength.CompatibleId, hit.Level);
        Assert.False(preview.IsInstallable);
    }

    [Fact]
    public void SyntheticOldOrMalformedReportCannotBypassPciSafetyFilter()
    {
        var device = Device(GpuClass, GpuClass);
        var declared = new InfModelEvidence.DeclaredId(
            "Models.NTamd64", GpuClass);
        var entry = new OemZipInfEvidence.InfEntry(
            "generic.inf", [declared], "Synthetic legacy metadata");
        var report = new OemZipInfEvidence.Report([entry], []);
        Assert.Empty(OemHardwareEvidence.Compare(report, [device]).Matches);

        var injected = report with
        {
            HasTargetedDeviceMatches = true,
            TargetedMatches =
                new Dictionary<string, IReadOnlyList<InfModelEvidence.DeclaredId>>
                {
                    ["generic.inf"] = [declared]
                }
        };
        Assert.Empty(OemHardwareEvidence.Compare(injected, [device]).Matches);
    }

    [Fact]
    public void LateVendorIdsAfterFiveHundredPreviewNeedTargetedInspection()
    {
        var unrelated = string.Join("\n", Enumerable.Range(1, 550)
            .Select(i => $"Model{i}=Install,PCI\\VEN_9999&DEV_{i:X4}"));
        var inf = Inf(@"PCI\VEN_9999&DEV_0000") +
            "\n[Models.NTamd64]\n" + unrelated +
            "\nClass=Install," + GpuClass +
            "\nGpu=Install," + Gpu +
            "\nWifi=Install," + Killer +
            "\nAudio=Install," + Audio;

        var devices = new[]
        {
            Device(Gpu, GpuClass, "NVIDIA"),
            Device(Killer, NetworkClass, "Killer"),
            Device(Audio, @"HDAUDIO\FUNC_01", "Realtek")
        };
        using var zip = Zip(("large.inf", inf));
        var preview = OemZipInfEvidence.Inspect(zip);
        Assert.Equal(InfModelEvidence.MaximumPreviewIds,
            Assert.Single(preview.InfFiles).DeclaredIds.Count);
        var early = OemHardwareEvidence.Compare(preview, devices);
        Assert.Empty(early.Matches);
        Assert.True(early.HasUnexaminedInf);

        zip.Position = 0;
        var targeted = OemZipInfEvidence.Inspect(zip, devices);
        Assert.True(targeted.HasTargetedDeviceMatches);
        var report = OemHardwareEvidence.Compare(targeted, devices);
        Assert.Equal(3, report.Matches.Count);
        Assert.All(report.Matches, row =>
            Assert.Equal(OemHardwareEvidence.Strength.HardwareId, row.Level));
        Assert.False(report.HasUnexaminedInf);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE_INSTANCE", string.Join(" ",
            report.Matches.Select(row => row.DeviceName)));
    }
}
