using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// OEM companion panels report actual PnP vendor counts, not driver
/// availability. A stale installed provider must not count a Realtek
/// codec as NVIDIA, Intel or AMD, even if its name is misleading.
/// </summary>
public sealed class OemCompanionDeviceCoverageTests
{
    private const string NvidiaGpu = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaAudio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string NvidiaSwc = @"SWC\VEN_10DE&DEV_28A0";
    private const string IntelGpu = @"PCI\VEN_8086&DEV_46A6";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string IntelBt = @"USB\VID_8087&PID_0033";
    private const string AmdGpu = @"PCI\VEN_1002&DEV_73DF";
    private const string AmdChipset = @"PCI\VEN_1022&DEV_15E3";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Qualcomm = @"USB\VID_0CF3&PID_E300";

    private static DriverInfo Device(string instance, string id,
        string manufacturer = "Microsoft", string deviceClass = "System") =>
        new(instance, "Generic Windows Device", manufacturer,
            deviceClass, manufacturer, "10.0.0.1", null,
            "PRIVATE_OEM.inf", true, true, 0, [id], []);

    [Theory]
    [InlineData(Realtek, "NVIDIA", 0)]
    [InlineData(Realtek, "Intel Corporation", 0)]
    [InlineData(Realtek, "AMD Radeon Software", 0)]
    [InlineData(Qualcomm, "NVIDIA", 0)]
    [InlineData(Qualcomm, "Intel Corporation", 0)]
    [InlineData(NvidiaAudio, "AMD Software", 1)]
    [InlineData(IntelBt, "NVIDIA", 0)]
    public void VerifiedHardwareVendorWinsOverWrongInstalledProvider(
        string model, string stale, int expectedNvidia)
    {
        var device = Device("PRIVATE_HARDWARE_INSTANCE", model, stale);
        var known = PnpKnownVendorIdentity.FromDriver(device);
        Assert.NotNull(known);

        // The individual detectors intentionally retain legacy provider
        // fallback for unrecognized devices, but OEM counts must not.
        var nvidia = NvidiaHardwareDetector.CountNvidiaDevices([device]);
        var intel = IntelHardwareDetector.CountIntelDevices([device]);
        var amd = AmdHardwareDetector.CountAmdDevices([device]);
        Assert.Equal(known == PnpKnownVendorIdentity.Vendor.Nvidia ? 1 : 0,
            nvidia);
        Assert.Equal(known == PnpKnownVendorIdentity.Vendor.Intel ? 1 : 0,
            intel);
        Assert.Equal(known == PnpKnownVendorIdentity.Vendor.Amd ? 1 : 0,
            amd);
        Assert.Equal(expectedNvidia, nvidia);
    }

    [Fact]
    public void ExactPnpInstancesAreDeduplicatedAcrossIdenticalRows()
    {
        var nvidia = Device("PRIVATE_GPU_INSTANCE", NvidiaGpu);
        var repeated = nvidia with
        {
            DeviceId = "private_gpu_instance",
            DriverProvider = "NVIDIA",
            Manufacturer = "NVIDIA"
        };
        var intel = Device("PRIVATE_KILLER_INSTANCE", Killer);
        var amd = Device("PRIVATE_AMD_INSTANCE", AmdChipset);

        var all = new[] { nvidia, repeated, intel, intel, amd, amd };
        Assert.Equal(1, NvidiaHardwareDetector.CountNvidiaDevices(all));
        Assert.Equal(1, IntelHardwareDetector.CountIntelDevices(all));
        Assert.Equal(1, AmdHardwareDetector.CountAmdDevices(all));
        Assert.Equal(0, NvidiaHardwareDetector.CountNvidiaDevices(
            [nvidia with { DeviceId = "" }]));
    }

    [Fact]
    public void ConflictingHardwareIdsFailClosedForAllCompanionVendors()
    {
        var conflict = Device("PRIVATE_CONFLICT", NvidiaGpu, "NVIDIA") with
        {
            HardwareIds = [NvidiaGpu, IntelGpu, AmdGpu]
        };
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Conflicting,
            PnpKnownVendorIdentity.FromDriver(conflict));
        Assert.Equal(0, NvidiaHardwareDetector.CountNvidiaDevices([conflict]));
        Assert.Equal(0, IntelHardwareDetector.CountIntelDevices([conflict]));
        Assert.Equal(0, AmdHardwareDetector.CountAmdDevices([conflict]));
    }

    [Fact]
    public void ContradictoryRowsForSameDeviceCannotVoteByArrivalOrder()
    {
        var nvidia = Device("PRIVATE_SAME_PNP_ID", NvidiaGpu);
        var realtek = Device("private_same_pnp_id", Realtek, "NVIDIA");
        foreach (var list in new[]
        {
            new[] { nvidia, realtek },
            [realtek, nvidia]
        })
        {
            Assert.Equal(0, NvidiaHardwareDetector.CountNvidiaDevices(list));
            Assert.Equal(0, IntelHardwareDetector.CountIntelDevices(list));
            Assert.Equal(0, AmdHardwareDetector.CountAmdDevices(list));
        }
    }

    [Theory]
    [InlineData(NvidiaGpu, "NVIDIA")]
    [InlineData(NvidiaAudio, "NVIDIA")]
    [InlineData(NvidiaSwc, "NVIDIA")]
    [InlineData(@"ACPI\NVDA0820", "NVIDIA")]
    [InlineData(@"USB\VID_0955&PID_7019", "NVIDIA")]
    [InlineData(IntelGpu, "Intel")]
    [InlineData(IntelBt, "Intel")]
    [InlineData(Killer, "Intel")]
    [InlineData(@"SWC\VEN_8086&DEV_51F1", "Intel")]
    [InlineData(AmdGpu, "AMD")]
    [InlineData(AmdChipset, "AMD")]
    [InlineData(@"SWC\VEN_1022&DEV_15E3", "AMD")]
    public void RealPnpVendorsAreCountedEvenWithMicrosoftGenericMetadata(
        string model, string expected)
    {
        var device = Device("PRIVATE_INSTANCE", model);
        var nvidia = NvidiaHardwareDetector.CountNvidiaDevices([device]);
        var intel = IntelHardwareDetector.CountIntelDevices([device]);
        var amd = AmdHardwareDetector.CountAmdDevices([device]);

        Assert.Equal(expected == "NVIDIA" ? 1 : 0, nvidia);
        Assert.Equal(expected == "Intel" ? 1 : 0, intel);
        Assert.Equal(expected == "AMD" ? 1 : 0, amd);
    }

    [Fact]
    public void PnpHardwareOutranksOppositeCompatibleVendor()
    {
        var nvidia = Device("PRIVATE_GPU", NvidiaGpu) with
        {
            CompatibleIds = [AmdGpu],
            DriverProvider = "AMD"
        };
        Assert.Equal(1, NvidiaHardwareDetector.CountNvidiaDevices([nvidia]));
        Assert.Equal(0, AmdHardwareDetector.CountAmdDevices([nvidia]));

        var intel = Device("PRIVATE_INTEL", IntelGpu) with
        {
            CompatibleIds = [NvidiaGpu],
            DriverProvider = "NVIDIA"
        };
        Assert.Equal(1, IntelHardwareDetector.CountIntelDevices([intel]));
        Assert.Equal(0, NvidiaHardwareDetector.CountNvidiaDevices([intel]));
    }

    [Fact]
    public void UnknownPnpRetainsLegacyVendorFallbackButForeignPciDoesNot()
    {
        var fallback = Device("PRIVATE_UNKNOWN", @"ROOT\NONSTANDARD",
            "NVIDIA Corporation");
        Assert.Null(PnpKnownVendorIdentity.FromDriver(fallback));
        Assert.Equal(1, NvidiaHardwareDetector.CountNvidiaDevices([fallback]));
        Assert.Equal(0, IntelHardwareDetector.CountIntelDevices([fallback]));

        var foreign = Device("PRIVATE_FOREIGN", @"PCI\VEN_9999&DEV_0001",
            "NVIDIA");
        Assert.Null(PnpKnownVendorIdentity.FromDriver(foreign));
        Assert.Equal(0, NvidiaHardwareDetector.CountNvidiaDevices([foreign]));

        var intelFallback = Device("PRIVATE_INTEL_UNKNOWN",
            @"ROOT\GENERIC", "Intel Corporation");
        Assert.Equal(1, IntelHardwareDetector.CountIntelDevices([intelFallback]));

        var amdFallback = Device("PRIVATE_AMD_UNKNOWN",
            @"ROOT\GENERIC", "AMD Radeon");
        Assert.Equal(1, AmdHardwareDetector.CountAmdDevices([amdFallback]));
    }

    [Fact]
    public void LargeIntelInventoryDoesNotInflateNvidiaOrAmd()
    {
        var intel = Enumerable.Range(1, 200).Select(i =>
            Device($"PRIVATE_INTEL_INSTANCE_{i}",
                $@"PCI\VEN_8086&DEV_{i:X4}", "Microsoft"));
        var realtek = Device("PRIVATE_AUDIO", Realtek, "NVIDIA");
        var nvidia = Device("PRIVATE_GPU", NvidiaGpu);
        var amd = Device("PRIVATE_CHIPSET", AmdChipset);
        var all = intel.Concat([realtek, nvidia, amd]).ToArray();

        Assert.Equal(200, IntelHardwareDetector.CountIntelDevices(all));
        Assert.Equal(1, NvidiaHardwareDetector.CountNvidiaDevices(all));
        Assert.Equal(1, AmdHardwareDetector.CountAmdDevices(all));
        Assert.Equal(0, AmdHardwareDetector.CountAmdDevices(
            [realtek]));
        // No serial, title or OEM INF path is ever emitted by a count.
    }


    [Fact]
    public void AsciiPaddedSameInstanceIsOneCompanionDevice()
    {
        var original = Device("PRIVATE_INSTANCE", NvidiaGpu);
        var alias = original with { DeviceId = " private_instance " };
        Assert.Equal(1, NvidiaHardwareDetector.CountNvidiaDevices(
            [original, alias]));
        Assert.Single(PnpDistinctTrustedDevices.Create(
            [original, alias]));
    }

    [Fact]
    public void ConflictingSameVendorModelsRejectAllCompanions()
    {
        // Both NVIDIA primary models have the SAME vendor, so a
        // vendor-only consensus would incorrectly count one GPU.
        var a = Device("PRIVATE_SAME_GPU", NvidiaGpu);
        var b = Device(" private_same_gpu ",
            @"PCI\VEN_10DE&DEV_1E84", "NVIDIA");

        foreach (var snapshots in new[]
        {
            new[] { a, b },
            new[] { b, a }
        })
        {
            Assert.Empty(PnpDistinctTrustedDevices.Create(snapshots));
            Assert.Equal(0,
                NvidiaHardwareDetector.CountNvidiaDevices(snapshots));
            Assert.Equal(0,
                IntelHardwareDetector.CountIntelDevices(snapshots));
            Assert.Equal(0,
                AmdHardwareDetector.CountAmdDevices(snapshots));
        }
    }

    [Fact]
    public void ConflictingSameVendorIntelAndAmdModelsAreNotDevices()
    {
        var intel = Device("PRIVATE_INTEL", IntelGpu);
        var intelConflict = Device(" private_intel ",
            @"PCI\VEN_8086&DEV_46A8");
        var amd = Device("PRIVATE_AMD", AmdGpu);
        var amdConflict = Device(" private_amd ",
            @"PCI\VEN_1002&DEV_73A5");

        Assert.Equal(0, IntelHardwareDetector.CountIntelDevices(
            [intel, intelConflict]));
        Assert.Equal(0, AmdHardwareDetector.CountAmdDevices(
            [amd, amdConflict]));
    }

    [Fact]
    public void ConflictingOemSubsystemForSameModelIsNotCounted()
    {
        var qualifiedA = Device("PRIVATE_GPU",
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_123410DE");
        var qualifiedB = Device(" private_gpu ",
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_ABCD10DE");
        Assert.Empty(PnpDistinctTrustedDevices.Create(
            [qualifiedA, qualifiedB]));
        Assert.Equal(0, NvidiaHardwareDetector.CountNvidiaDevices(
            [qualifiedA, qualifiedB]));
    }

    [Fact]
    public void CoherentBareAndOneQualifiedModelStillCountOneGpu()
    {
        var bare = Device("PRIVATE_GPU", NvidiaGpu);
        var qualified = Device(" private_gpu ",
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_123410DE");
        Assert.Single(PnpDistinctTrustedDevices.Create([bare, qualified]));
        Assert.Equal(1, NvidiaHardwareDetector.CountNvidiaDevices(
            [bare, qualified]));
    }

    [Fact]
    public void PaddedOppositeVendorsCannotBothClaimSameInstance()
    {
        var intel = Device("PRIVATE_SHARED", IntelGpu);
        var nvidia = Device(" private_shared ", NvidiaGpu);
        Assert.Empty(PnpDistinctTrustedDevices.Create([intel, nvidia]));
        Assert.Equal(0, NvidiaHardwareDetector.CountNvidiaDevices(
            [nvidia, intel]));
        Assert.Equal(0, IntelHardwareDetector.CountIntelDevices(
            [intel, nvidia]));
    }

    [Fact]
    public void UnknownModelLegacyFallbackRemainsReadOnly()
    {
        var first = Device("PRIVATE_UNKNOWN", @"ROOT\NONSTANDARD",
            "NVIDIA Corporation");
        var padded = first with { DeviceId = " private_unknown " };
        Assert.Equal(1, NvidiaHardwareDetector.CountNvidiaDevices(
            [first, padded]));
        Assert.Equal(0, IntelHardwareDetector.CountIntelDevices(
            [first, padded]));
    }

    [Fact]
    public void OnlySupportedCompanionTargetsAreAccepted()
    {
        var device = Device("PRIVATE_LOGITECH", @"HID\VID_046D&PID_C534");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OemCompanionDeviceCoverage.Count(
                [device], PnpKnownVendorIdentity.Vendor.Logitech,
                _ => true));
    }
}
