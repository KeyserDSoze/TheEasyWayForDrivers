using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Missing WMI names must not hide device handoffs, accidentally classify
/// a serial as a manufacturer name, or disclose PnP instance IDs in Home.
/// </summary>
public sealed class PnpDeviceDisplayNameTests
{
    private const string PrivateGpuInstance =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA\PRIVATE_GPU_SERIAL";
    private const string PrivateKillerInstance =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56\PRIVATE_WIFI_SERIAL";
    private const string PrivateAudioInstance =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295\PRIVATE_AUDIO_SERIAL";

    private static DriverInfo Device(
        string? name, string? pnpClass, string instance, string hardware) =>
        new(instance,
            PnpDeviceDisplayName.Create(name, pnpClass, instance),
            "Microsoft", pnpClass, "Microsoft", "1.0.0.0",
            null, null, true, true, 0, [hardware], []);

    [Theory]
    [InlineData("Display", "Scheda video (nome non disponibile)")]
    [InlineData("Net", "Scheda di rete (nome non disponibile)")]
    [InlineData("Bluetooth", "Dispositivo Bluetooth (nome non disponibile)")]
    [InlineData("MEDIA", "Dispositivo audio (nome non disponibile)")]
    [InlineData("System", "Dispositivo di sistema (nome non disponibile)")]
    [InlineData("SoftwareComponent", "Componente software (nome non disponibile)")]
    [InlineData("Extension", "Estensione driver (nome non disponibile)")]
    [InlineData("Unknown", "Dispositivo Windows (nome non disponibile)")]
    [InlineData("Custom\0Class", "Dispositivo Windows (nome non disponibile)")]
    [InlineData(null, "Dispositivo Windows (nome non disponibile)")]
    public void NullNameUsesOnlyAllowlistedPnPClassLabels(
        string? pnpClass, string expected)
    {
        var actual = PnpDeviceDisplayName.Create(null, pnpClass, PrivateGpuInstance);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("VEN_", actual);
        Assert.DoesNotContain("PRIVATE_", actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\t")]
    [InlineData(PrivateGpuInstance)]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0")]
    [InlineData(@"USB\VID_1234&PID_ABCD\SERIAL")]
    [InlineData(@"C:\Users\Private\driver.inf")]
    [InlineData(@"\\SERVER\PRIVATE_SERIAL")]
    [InlineData("https://private-server.example/serial")]
    [InlineData("https://private-server.example")]
    [InlineData("NVIDIA driver \u202E exe")]
    [InlineData("Controller\r\nPrivate instance")]
    [InlineData("GPU VEN_10DE&DEV_28A0")]
    [InlineData("GPU SUBSYS_16A217AA")]
    [InlineData("USB VID_0CF3 device")]
    [InlineData("Device SERIAL_ZXY123")]
    public void PnpInstancePathsAndPrivateTokensNeverBecomeDeviceNames(
        string untrustedName)
    {
        var label = PnpDeviceDisplayName.Create(
            untrustedName, "Display", PrivateGpuInstance);
        Assert.Equal("Scheda video (nome non disponibile)", label);
        Assert.DoesNotContain("PRIVATE_", label);
        Assert.DoesNotContain("VEN_", label);
    }

    [Fact]
    public void ExcessiveOrOneCharacterNameIsNotTreatedAsEvidence()
    {
        Assert.Equal("Dispositivo Windows (nome non disponibile)",
            PnpDeviceDisplayName.Create("X", null));
        Assert.Equal("Dispositivo di sistema (nome non disponibile)",
            PnpDeviceDisplayName.Create(
                new string('X', PnpDeviceDisplayName.MaximumNameLength + 1),
                "System"));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU")]
    [InlineData("Intel(R) Killer(TM) Wi-Fi AX1675i")]
    [InlineData("Realtek(R) Audio")]
    [InlineData("Microsoft Basic Display Adapter")]
    [InlineData("Intel(R) Wi-Fi 6E AX211 160MHz")]
    public void OrdinaryDriverNamesSurviveUnchanged(string name)
    {
        Assert.Equal(name, PnpDeviceDisplayName.Create(
            name, "Display", PrivateGpuInstance));
        Assert.Equal(name, PnpDeviceDisplayName.Create(
            " " + name + "  ", "Display", PrivateGpuInstance));
    }

    [Fact]
    public void GenericClassStillRecognizesNvidiaKillerAndRealtekByPnP()
    {
        var gpu = Device(null, "Display", PrivateGpuInstance,
            @"PCI\VEN_10DE&DEV_28A0");
        var killer = Device("", "Net", PrivateKillerInstance,
            @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56");
        var audio = Device(PrivateAudioInstance, "MEDIA",
            PrivateAudioInstance, @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295");

        var candidates = VendorSourceDiscovery.Find([gpu, killer, audio], []);

        Assert.Equal(3, candidates.Count);
        Assert.Contains(candidates, row => row.Vendor == "NVIDIA" &&
            row.OfficialUrl == VendorSourceDiscovery.NvidiaDriversUrl &&
            row.DeviceName == "Scheda video (nome non disponibile)");
        Assert.Contains(candidates, row => row.Vendor == "Killer / Intel" &&
            row.OfficialUrl == VendorSourceDiscovery.IntelWifiUrl &&
            row.DeviceName == "Scheda di rete (nome non disponibile)");
        Assert.Contains(candidates, row => row.Vendor == "Realtek" &&
            row.OfficialUrl == VendorSourceDiscovery.RealtekSupportUrl &&
            row.DeviceName == "Dispositivo audio (nome non disponibile)");
        Assert.All(candidates, row =>
        {
            Assert.False(row.IsVendorApplicable);
            Assert.Equal(0, row.WuaMatches);
            Assert.DoesNotContain("PRIVATE_", row.Title);
            Assert.DoesNotContain("PRIVATE_", row.Detail);
            Assert.DoesNotContain("SUBSYS_", row.Detail);
            Assert.Contains("nessun aggiornamento OEM confermato",
                row.StatusText);
        });
    }

    [Fact]
    public void MissingFriendlyNameDoesNotDestroyExactWuaCorrelation()
    {
        const string hardware = @"PCI\VEN_10DE&DEV_28A0";
        var device = Device(null, "Display", PrivateGpuInstance, hardware);
        var wua = new DriverUpdateInfo(
            "00000000-0000-4000-8000-000000000312",
            "NVIDIA candidate from Windows Update",
            null, "Display", "NVIDIA", null, null,
            false, "NVIDIA", null, hardware, null);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(device, wua));
        var hint = Assert.Single(VendorSourceDiscovery.Find([device], [wua]));
        Assert.Equal("NVIDIA", hint.Vendor);
        Assert.Equal(1, hint.WuaMatches);
        Assert.False(hint.IsVendorApplicable);
        Assert.DoesNotContain("PRIVATE_", hint.DeviceName);
        Assert.DoesNotContain("PRIVATE_", hint.Detail);
    }

    [Fact]
    public void WmiNameMissingNoLongerProducesEmptyVendorHandoffRows()
    {
        var withoutSafeName = new DriverInfo(
            PrivateGpuInstance, "", "Microsoft", "Display", "Microsoft",
            "1.0.0.0", null, null, true, true, 0,
            [@"PCI\VEN_10DE&DEV_28A0"], []);
        var missingNameHandoff = Assert.Single(
            VendorSourceDiscovery.Find([withoutSafeName], []));
        Assert.Equal("NVIDIA", missingNameHandoff.Vendor);
        Assert.Equal("Scheda video (nome non disponibile)",
            missingNameHandoff.DeviceName);
        Assert.False(missingNameHandoff.IsVendorApplicable);
        Assert.DoesNotContain("PRIVATE_", missingNameHandoff.Detail);

        var withSafeName = withoutSafeName with
        {
            Name = PnpDeviceDisplayName.Create("", "Display",
                withoutSafeName.DeviceId)
        };
        var result = Assert.Single(
            VendorSourceDiscovery.Find([withSafeName], []));
        Assert.Equal("NVIDIA", result.Vendor);
        Assert.Equal("Scheda video (nome non disponibile)",
            result.DeviceName);
    }
}
