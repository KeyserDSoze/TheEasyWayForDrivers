using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class HomeVendorLocalRankLookupTests
{
    private static DriverInfo Device(
        string instance, string title, string category, string hardware,
        uint problem = 0) =>
        new(instance, title, "Microsoft", category, "Microsoft",
            "31.0.0.1", null, "oem14.inf", true, true, problem,
            [hardware], []);

    private static readonly DriverInfo NvidiaGpu = Device(
        "PRIVATE_GPU", "Microsoft Basic Display Adapter", "Display",
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA");
    private static readonly DriverInfo NvidiaAudio = Device(
        "PRIVATE_GPU_AUDIO", "High Definition Audio Device", "MEDIA",
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B", 28);
    private static readonly DriverInfo Killer = Device(
        "PRIVATE_KILLER", "Killer Wi-Fi 6E", "Net",
        @"PCI\VEN_8086&DEV_51F1");
    private static readonly DriverInfo IntelGraphics = Device(
        "PRIVATE_INTEL_GPU", "Microsoft Basic Display Adapter", "Display",
        @"PCI\VEN_8086&DEV_46A6");
    private static readonly DriverInfo IntelBluetooth = Device(
        "PRIVATE_INTEL_BT", "Generic Bluetooth Adapter", "Bluetooth",
        @"USB\VID_8087&PID_0033", 28);
    private static readonly DriverInfo RealtekAudio = Device(
        "PRIVATE_REALTEK_AUDIO", "High Definition Audio Device", "MEDIA",
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295");
    private static readonly DriverInfo RealtekNetwork = Device(
        "PRIVATE_REALTEK_NET", "Ethernet Controller", "Net",
        @"PCI\VEN_10EC&DEV_8168", 28);

    [Fact]
    public void NvidiaCardSelectsGpuNotBrokenHdAudioNode()
    {
        var result = HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [NvidiaAudio, NvidiaGpu], "NVIDIA", true);
        Assert.Same(NvidiaGpu, result);
    }

    [Fact]
    public void IntelKillerCardSelectsKillerWifiAheadOfIntelGpuAndBluetooth()
    {
        var result = HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [IntelGraphics, IntelBluetooth, Killer],
            "Intel / Killer", true);
        Assert.Same(Killer, result);
    }

    [Fact]
    public void RealtekCardSelectsMediaCodecAheadOfNetworkEvenWhenNetBroken()
    {
        var result = HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [RealtekNetwork, RealtekAudio], "Realtek", true);
        Assert.Same(RealtekAudio, result);
    }

    [Fact]
    public void BrokenOrMissingInventoryNeverEnablesLocalAction()
    {
        var devices = new[] { NvidiaGpu, Killer, RealtekAudio };
        foreach (var group in new[] { "NVIDIA", "Intel / Killer", "Realtek" })
        {
            Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                devices, group, false));
        }

        var cards = HomeVendorCoverageSummary.Create(devices, null, false);
        Assert.All(cards, card =>
        {
            Assert.False(card.HasLocalDevice);
            Assert.False(card.CanInspectLocalDrivers);
            Assert.False(card.IsInstallable);
        });
    }

    [Fact]
    public void MissingVendorAndIntelHostBridgeCannotTriggerAnotherDevice()
    {
        var intelBridge = Device("PRIVATE_BRIDGE", "Intel Host Bridge",
            "System", @"PCI\VEN_8086&DEV_1234");
        var devices = new[] { NvidiaGpu, intelBridge };
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            devices, "Intel / Killer", true));
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            devices, "Realtek", true));
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            devices, "Unrecognized vendor", true));
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            devices, "NVIDIA Driver Update", true));
    }

    [Fact]
    public void DuplicatePnpInstancesAndGenericNamesUseStableSelection()
    {
        var devices = new[]
        {
            NvidiaAudio,
            NvidiaGpu with { DeviceId = "private_gpu" },
            NvidiaGpu,
            RealtekNetwork,
            RealtekAudio
        };
        var selected = HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            devices, "NVIDIA", true);
        Assert.Equal("private_gpu", selected!.DeviceId);
        var cards = HomeVendorCoverageSummary.Create(devices, null, true);
        Assert.Equal(1, cards.Count(x => x.Group == "NVIDIA" &&
            x.CanInspectLocalDrivers));
        Assert.True(cards.Single(x => x.Group == "Realtek").CanInspectLocalDrivers);
        Assert.False(cards.Single(x => x.Group == "Intel / Killer")
            .CanInspectLocalDrivers);
        Assert.All(cards, card => Assert.False(card.IsInstallable));
        Assert.DoesNotContain("PRIVATE",
            string.Join(" ", cards.Select(row => row.DeviceStatus)));
    }

    [Fact]
    public void SameVendorDevicesPreferHardwareClassThenWindowsProblem()
    {
        var first = NvidiaGpu with { DeviceId = "Z_GPU" };
        var second = NvidiaGpu with { DeviceId = "A_GPU",
            ConfigManagerErrorCode = 28 };
        var result = HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [first, second, NvidiaAudio], "NVIDIA", true);
        Assert.Same(second, result);
    }
}
