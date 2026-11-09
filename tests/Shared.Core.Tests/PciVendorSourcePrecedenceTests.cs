using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// The actual PCI device vendor is stronger than stale WMI/provider names.
/// Handoff, version evidence and eight-row diagnostic must stay aligned.
/// These fixtures never represent verified OEM or installable packages.
/// </summary>
public sealed class PciVendorSourcePrecedenceTests
{
    private static DriverInfo Device(string id, string title,
        string provider, string deviceClass, string? instance = null) =>
        new(instance ?? "PRIVATE_INSTANCE", title, provider, deviceClass,
            provider, "31.0.0.111", null, "oem42.inf",
            true, true, 0, [id], []);

    private static DriverUpdateInfo Wua(string id) =>
        new(Guid.NewGuid().ToString("D"), "WUA Windows driver", null,
            "Display", "Microsoft", null, null, false, "Microsoft",
            null, id, null, SearchSource: "Windows Update online");

    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_46A6", "NVIDIA GeForce GPU",
        "NVIDIA", "Display", "Intel", 0, 1)]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0", "Intel UHD Graphics",
        "Intel", "Display", "NVIDIA", 1, 0)]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0", "AMD Radeon Graphics",
        "AMD", "Display", "NVIDIA", 1, 0)]
    [InlineData(@"PCI\VEN_1002&DEV_7480", "Intel Graphics",
        "Intel", "Display", "AMD", 0, 0)]
    [InlineData(@"PCI\VEN_10EC&DEV_8125", "NVIDIA Network Adapter",
        "NVIDIA", "Net", "Realtek", 0, 0)]
    [InlineData(@"PCI\VEN_168C&DEV_003E", "Intel Wi-Fi Network Adapter",
        "Intel", "Net", "Qualcomm / Atheros", 0, 0)]
    public void ConflictingPciVendorBeatsBrandOnHandoffAndInstalledVersions(
        string hardwareId, string name, string provider, string deviceClass,
        string correctVendor, int nvidiaGpus, int intelGpus)
    {
        var device = Device(hardwareId, name, provider, deviceClass);
        var handoff = Assert.Single(VendorSourceDiscovery.Find([device], []));
        var evidence = Assert.Single(VendorDriverEvidenceFinder.Find(
            [device], [Wua(hardwareId)]));
        var matrix = ReferenceDriverCoverage.Create(
            [device], new DriverSearchReport([Wua(hardwareId)],
                [new DriverSearchPassStatus(
                    "Windows Update online", "Completed", 1, "OK")]));

        Assert.Equal(correctVendor, handoff.Vendor);
        Assert.Equal(correctVendor, evidence.Vendor);
        Assert.Equal(correctVendor,
            VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Equal(correctVendor, DriverSourceAdvisor.GetSource(device, []));
        Assert.Equal(1, evidence.CorrelatedWuaCandidates);
        Assert.Equal(0, handoff.WuaMatches);
        Assert.False(handoff.IsVendorApplicable);
        Assert.Equal(nvidiaGpus, matrix[5].Devices);
        Assert.Equal(nvidiaGpus, matrix[5].WindowsDriverCandidates);
        Assert.Equal(intelGpus, matrix[0].Devices);
        Assert.Equal(intelGpus, matrix[0].WindowsDriverCandidates);

        var copy = ScanCoverageText.Create(null, [], 1, 0, 0,
            [device], inventoryComplete: true);
        Assert.DoesNotContain("PRIVATE_INSTANCE", copy);
        Assert.DoesNotContain(hardwareId, copy);
        Assert.DoesNotContain("oem42.inf", copy);
    }

    [Fact]
    public void ValidNonPciNvidiaDchAudioAndPlatformRemainRecognized()
    {
        var audio = Device(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B",
            "High Definition Audio Device", "Microsoft", "MEDIA");
        var platform = Device(@"ACPI\NVDA0820&REV_01",
            "System Controller", "Microsoft", "System",
            "PRIVATE_PLATFORM");
        var handoffs = VendorSourceDiscovery.Find([audio, platform], []);
        Assert.Equal(2, handoffs.Count);
        Assert.All(handoffs, h => Assert.Equal("NVIDIA", h.Vendor));
        var rows = ReferenceDriverCoverage.Create([audio, platform], null);
        Assert.Equal(1, rows[6].Devices);
        Assert.Equal(1, rows[7].Devices);
        Assert.Equal(0, rows.Sum(x => x.WindowsDriverCandidates));
        Assert.DoesNotContain("PRIVATE_PLATFORM", string.Join(" ",
            handoffs.Select(x => x.StatusText)));
    }

    [Fact]
    public void GenuineKillerIntelPciRemainsIntelSupportAndWifiCategory()
    {
        var killer = Device(
            @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56",
            "Killer Wi-Fi 6E AX1675i", "Intel", "Net");
        var handoff = Assert.Single(VendorSourceDiscovery.Find([killer], []));
        Assert.Equal("Killer / Intel", handoff.Vendor);
        Assert.Equal(VendorSourceDiscovery.IntelWifiUrl, handoff.OfficialUrl);
        Assert.Equal("Killer / Intel", Assert.Single(
            VendorDriverEvidenceFinder.Find([killer], [])).Vendor);
        Assert.Equal(1, ReferenceDriverCoverage.Create(
            [killer], null)[4].Devices);
    }

    [Theory]
    [InlineData(@"PCI\VEN_80860&DEV_46A6")]
    [InlineData(@"PCI\VEN_8086&DEV_46A60")]
    [InlineData(@"PCI\VEN_8086&DEV_46AG")]
    [InlineData(@"PCI\VEN_8086&DEV_46A6\PRIVATE_INSTANCE")]
    [InlineData(@"PCI\VEN_8086")]
    public void MalformedPciVendorIsNotTrustedAsHardwareEvidence(string id)
    {
        var mislabeled = Device(id, "NVIDIA GeForce GPU", "NVIDIA", "Display");
        // No public brand evidence: malformed VEN/DEV must not become
        // a verified vendor via the PnP source or the eight-row matrix.
        var neutral = mislabeled with
        {
            Name = "PCI Device",
            DriverProvider = "Microsoft",
            Manufacturer = "Microsoft"
        };
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(
            neutral));
        Assert.Equal(0, ReferenceDriverCoverage.Create(
            [neutral], null).Sum(x => x.Devices));
    }
}
