using Microsoft.Extensions.Logging.Abstractions;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// The actual provider response must use the same strict PnP vendor count
/// as Home, not the stale manufacturer's name or duplicate WMI records.
/// Companion registry state is intentionally not asserted on CI machines.
/// </summary>
public sealed class OemCompanionProviderCountTests
{
    private static DriverInfo Device(string id, string model,
        string maker = "Microsoft") =>
        new(id, "Generic Windows Device", maker, "System", maker,
            "1.0", null, "PRIVATE_PACKAGE.inf", true, true, 0,
            [model], []);

    [Fact]
    public async Task NvidiaProviderCountsActualGpuNotStaleNvidiaCodec()
    {
        var gpu = Device("PRIVATE_NVIDIA_GPU",
            @"PCI\VEN_10DE&DEV_28A0");
        var realtek = Device("PRIVATE_REALTEK_CODEC",
            @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295", "NVIDIA");
        var provider = new NvidiaOemDriverProvider(
            NullLogger<NvidiaOemDriverProvider>.Instance);

        var status = await provider.GetStatusAsync(
            [gpu, gpu, realtek], CancellationToken.None);

        Assert.Equal("nvidia", status.ProviderId);
        Assert.Equal(1, status.DetectedDeviceCount);
        Assert.True(status.IsApplicable);
        Assert.Equal(NvidiaOemDriverProvider.OfficialSupportUrl,
            status.OfficialSupportUrl);
        Assert.DoesNotContain("PRIVATE", status.Message);
    }

    [Fact]
    public async Task IntelProviderCountsKillerNotStaleIntelBluetooth()
    {
        var killer = Device("PRIVATE_KILLER",
            @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56");
        var qualcomm = Device("PRIVATE_QUALCOMM",
            @"USB\VID_0CF3&PID_E300", "Intel Corporation");
        var provider = new IntelOemDriverProvider(
            NullLogger<IntelOemDriverProvider>.Instance);

        var status = await provider.GetStatusAsync(
            [killer, killer, qualcomm], CancellationToken.None);

        Assert.Equal("intel", status.ProviderId);
        Assert.Equal(1, status.DetectedDeviceCount);
        Assert.True(status.IsApplicable);
        Assert.Equal(IntelOemDriverProvider.OfficialSupportUrl,
            status.OfficialSupportUrl);
        Assert.DoesNotContain("PRIVATE", status.Message);
    }

    [Fact]
    public async Task AmdProviderRecognizes1022ChipsetEvenWithMicrosoftDriver()
    {
        var chipset = Device("PRIVATE_AMD_CHIPSET",
            @"PCI\VEN_1022&DEV_15E3");
        var intel = Device("PRIVATE_INTEL",
            @"PCI\VEN_8086&DEV_46A6", "AMD Radeon Software");
        var provider = new AmdOemDriverProvider(
            NullLogger<AmdOemDriverProvider>.Instance);

        var status = await provider.GetStatusAsync(
            [chipset, chipset, intel], CancellationToken.None);

        Assert.Equal("amd", status.ProviderId);
        Assert.Equal(1, status.DetectedDeviceCount);
        Assert.True(status.IsApplicable);
        Assert.Equal(AmdOemDriverProvider.OfficialSupportUrl,
            status.OfficialSupportUrl);
        Assert.DoesNotContain("PRIVATE", status.Message);
    }

    [Fact]
    public async Task ForeignVendorOnlyCannotMakeCompanionApplicable()
    {
        var realtek = Device("PRIVATE_REALTEK",
            @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295", "NVIDIA");
        var nvidia = new NvidiaOemDriverProvider(
            NullLogger<NvidiaOemDriverProvider>.Instance);
        var intel = new IntelOemDriverProvider(
            NullLogger<IntelOemDriverProvider>.Instance);
        var amd = new AmdOemDriverProvider(
            NullLogger<AmdOemDriverProvider>.Instance);

        foreach (var status in new[]
        {
            await nvidia.GetStatusAsync([realtek], CancellationToken.None),
            await intel.GetStatusAsync([realtek], CancellationToken.None),
            await amd.GetStatusAsync([realtek], CancellationToken.None)
        })
        {
            Assert.Equal(0, status.DetectedDeviceCount);
            Assert.False(status.IsApplicable);
            Assert.DoesNotContain("PRIVATE", status.Message);
        }
    }
}
