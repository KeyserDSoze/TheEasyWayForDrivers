using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// An INF declaring only a generic PCI CC class cannot identify a particular
/// laptop GPU/NIC or justify a higher-driver-version hint. Real VEN+DEV models
/// in the same ZIP or folder must remain discoverable.
/// </summary>
public sealed class OemPciClassOnlyEvidenceTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string GpuClass = @"PCI\CC_030000";
    private const string NetworkClass = @"PCI\CC_0280";

    private static DriverInfo Device(
        string instance, string hardware, string compatible, string name) =>
        new(instance, name, "Microsoft", "Unknown", "Microsoft",
            "31.0.0.0", null, "oem1.inf", true, true, 0,
            [hardware], [compatible]);

    private static DriverInfo GpuDevice() =>
        Device("PRIVATE_GPU_INSTANCE", Gpu, GpuClass, "Generic video adapter");

    private static DriverInfo KillerDevice() =>
        Device("PRIVATE_KILLER_INSTANCE", Killer, NetworkClass,
            "Generic network adapter");

    private static string Inf(string hardware, string? alias = null) => $"""
        [Version]
        Signature="$Windows NT$"
        Class=Display
        DriverVer=11/09/2026,32.0.16.2000
        [Manufacturer]
        %Vendor%=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        %Device%=Install,{hardware}{(alias is null ? "" : "," + alias)}
        """;

    private static MemoryStream Archive(params (string Name, string Text)[] files)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(),
                    new UTF8Encoding(false));
                writer.Write(text);
            }
        }
        memory.Position = 0;
        return memory;
    }

    [Theory]
    [InlineData(GpuClass)]
    [InlineData(NetworkClass)]
    [InlineData(@"PCI\VEN_10DE")]
    [InlineData(@"PCI\VEN_10DE&CC_0300")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&FAKE_PRIVATE")]
    public void GenericOrMalformedPciIdRemainsInfMetadataButNeverDeviceMatch(
        string genericId)
    {
        var text = Inf(genericId);
        var device = Device("PRIVATE_NODE", genericId, genericId,
            "Microsoft generic device");

        // Show the declaration in the INF preview rather than pretending
        // it was absent; NEVER use it to prove per-device applicability.
        var declared = Assert.Single(InfModelEvidence.ReadDeclaredIds(text));
        Assert.Equal(genericId.ToUpperInvariant(), declared.HardwareId);
        Assert.Equal(0, InfModelEvidence.MatchStrength(device, [declared]));
        Assert.Empty(InfModelEvidence.ReadMatchingIds(text, [device]));

        var preflight = InfPlatformPreflight.Assess(
            text, device, "x64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.NoHardwareMatch,
            preflight.Result);
        Assert.Equal(0, preflight.HardwareMatchStrength);
        Assert.DoesNotContain("PRIVATE", preflight.Explanation);
    }

    [Fact]
    public void ZipClassOnlyRowsNeverInflateNvidiaAndKillerPotentialUpdates()
    {
        using var zip = Archive(
            ("drivers/gpu-class.inf", Inf(GpuClass)),
            ("drivers/network-class.inf", Inf(NetworkClass)),
            ("drivers/nvidia.inf", Inf(Gpu)),
            ("drivers/killer.inf", Inf(Killer)));

        var result = LocalOemDriverComparison.Compare(
            zip, [GpuDevice(), KillerDevice()], "x64", 10, 0, 26100);

        Assert.Equal(4, result.InfExamined);
        Assert.Equal(4, result.InfWithDeclaredIds);
        Assert.Equal(2, result.TotalMatchedDevices);
        Assert.Equal(2, result.PotentialNewerDevices);
        Assert.Equal(0, result.AdditionalMatchesOmitted);
        Assert.All(result.Findings, finding =>
        {
            Assert.True(finding.HasExactHardwareId);
            Assert.True(finding.IsPotentialNewer);
            Assert.DoesNotContain("class.inf", finding.InfName);
        });
        Assert.Contains(result.Findings, x => x.InfName == "nvidia.inf");
        Assert.Contains(result.Findings, x => x.InfName == "killer.inf");
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE", result.Summary);
        Assert.DoesNotContain(Gpu, result.Summary);
    }

    [Fact]
    public void LocalFolderWithOnlyGenericPciClassInfsHasNoModelMatches()
    {
        var folder = Directory.CreateTempSubdirectory(
            "OmegaDrive-GenericPciClass-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "gpu.inf"),
                Inf(GpuClass));
            File.WriteAllText(Path.Combine(folder.FullName, "network.inf"),
                Inf(NetworkClass));

            var result = LocalOemFolderComparison.Compare(
                folder.FullName, [GpuDevice(), KillerDevice()],
                "x64", 10, 0, 26100);

            Assert.Equal(2, result.InfExamined);
            Assert.Equal(2, result.InfWithDeclaredIds);
            Assert.Empty(result.Findings);
            Assert.Equal(0, result.TotalMatchedDevices);
            Assert.Equal(0, result.PotentialNewerDevices);
            Assert.False(result.IsInstallable);
            Assert.DoesNotContain("PRIVATE", result.Summary);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void GenericPciClassCompatibleAliasDoesNotMatchWrongGpu()
    {
        var text = Inf(Gpu, GpuClass);
        var unrelated = Device("PRIVATE_OTHER_GPU",
            @"PCI\VEN_1002&DEV_164E&SUBSYS_00001002",
            GpuClass, "Generic video adapter");

        Assert.Equal(2, InfModelEvidence.ReadDeclaredIds(text).Count);
        Assert.Empty(InfModelEvidence.ReadMatchingIds(text, [unrelated]));
        Assert.Equal(InfPlatformPreflight.Verdict.NoHardwareMatch,
            InfPlatformPreflight.Assess(text, unrelated, "x64",
                10, 0, 26100, false).Result);

        var matched = InfModelEvidence.ReadMatchingIds(text, [GpuDevice()]);
        Assert.Equal(2, InfModelEvidence.MatchStrength(GpuDevice(), matched));
        Assert.Single(matched); // class alias remains preview-only evidence
    }
}
