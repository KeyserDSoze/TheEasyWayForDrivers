using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Hardware-vendor precedence: a USB NVIDIA device with a generic Windows
/// label retains OEM support, while stale provider text cannot inflate the
/// eight-category Intel/Killer/NVIDIA QA matrix.
/// </summary>
public sealed class NvidiaUsbAndVendorQaPrecedenceTests
{
    private const string NvidiaUsb = @"USB\VID_0955&PID_7019";
    private const string NvidiaGpu = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaAudio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string NvidiaSwc = @"SWC\VEN_10DE&DEV_28A0";
    private const string RealtekUsb = @"USB\VID_0BDA&PID_8771";
    private const string RealtekAudio = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string QualcommWifi = @"USB\VID_0CF3&PID_E300";
    private const string GuidOne = "00000000-0000-4000-8000-000000000349";

    private static DriverInfo Device(string hardware, string title,
        string driverClass, string manufacturer = "Microsoft",
        string? provider = null) =>
        new("PRIVATE_PNP_INSTANCE_SERIAL", title, manufacturer,
            driverClass, provider ?? manufacturer, "10.0", null,
            "oem42.inf", true, true, 0, [hardware], []);

    private static DriverSearchReport Report(params string[] hardwareIds) =>
        new(hardwareIds.Select((hardware, i) =>
            new DriverUpdateInfo(
                i == 0 ? GuidOne : Guid.NewGuid().ToString("D"),
                "Windows driver", null, "Display", "Microsoft",
                null, null, false, "Microsoft", null, hardware, null,
                SearchSource: "Windows Update online")).ToArray(),
            [new DriverSearchPassStatus(
                "Windows Update online", "Completed",
                hardwareIds.Length, "Ricerca eseguita.")]);

    [Theory]
    [InlineData(NvidiaUsb)]
    [InlineData(@"usb\vid_0955&pid_7019&rev_0200")]
    [InlineData(@"USB\VID_0955&PID_7019&MI_00")]
    [InlineData(@"USB\VID_0955&PID_7019&REV_0200&MI_00")]
    public void NvidiaUsbModelWithGenericWindowsNameGetsOfficialSupport(string id)
    {
        var device = Device(id, "Generic USB Device", "USB");
        Assert.True(NvidiaHardwareDetector.IsNvidiaDevice(device));
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Nvidia,
            PnpKnownVendorIdentity.FromDriver(device));
        var row = Assert.Single(VendorSourceDiscovery.Find([device], []));
        Assert.Equal("NVIDIA", row.Vendor);
        Assert.Equal(VendorSourceDiscovery.NvidiaDriversUrl, row.OfficialUrl);
        Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(row.OfficialUrl));
        Assert.Equal(0, row.WuaMatches);
        Assert.False(row.IsVendorApplicable);
        Assert.False(row.IsCatalogMetadata);
        Assert.Contains("nessun aggiornamento OEM confermato",
            row.StatusText);
        Assert.DoesNotContain("PRIVATE", row.Detail);
        Assert.Equal("NVIDIA", DriverSourceAdvisor.GetSource(device, []));

        // A vendor USB peripheral is NOT automatically an RTX GPU or
        // NVIDIA Platform Controller. This is a manual support source only.
        var categories = ReferenceDriverCoverage.Create(
            [device], Report(id));
        Assert.Equal(0, categories.Sum(row => row.Devices));
        Assert.Equal(0, categories.Sum(row => row.WindowsDriverCandidates));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            device, Assert.Single(Report(id).Updates)));
    }

    [Theory]
    [InlineData(@"USB\VID_0955")]
    [InlineData(@"USB\VID_0955&PID_")]
    [InlineData(@"USB\VID_0955&PID_701")]
    [InlineData(@"USB\VID_0955&PID_70190")]
    [InlineData(@"USB\VID_09550&PID_7019")]
    [InlineData(@"USB\VID_0955&PID_7019&FAKE_01")]
    [InlineData(@"USB\VID_0955&PID_7019\PRIVATE_SERIAL")]
    [InlineData(@"USB\VID_0955&PID_7019" + "\n")]
    [InlineData(@"USB\VID_0955&PID_7019" + "\u202e")]
    [InlineData(@"HID\VID_0955&UP:0001_U:0006")]
    [InlineData(@"PCI\VEN_0955&DEV_7019")]
    public void VendorOnlyMalformedAndForeignBusCannotInventNvidiaUsb(string id)
    {
        var device = Device(id, "Generic Windows Device", "USB");
        Assert.False(NvidiaHardwareDetector.IsNvidiaDevice(device));
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Empty(VendorSourceDiscovery.Find([device], []));
    }

    [Theory]
    [InlineData(RealtekAudio, "NVIDIA High Definition Audio",
        "MEDIA", "NVIDIA", 6)]
    [InlineData(RealtekUsb, "NVIDIA Display Controller",
        "Display", "NVIDIA", 5)]
    [InlineData(QualcommWifi, "Intel Wireless Bluetooth",
        "Bluetooth", "Intel", 1)]
    [InlineData(RealtekUsb, "Killer Wi-Fi",
        "Net", "Killer", 4)]
    public void ValidatedForeignModelOutranksStaleNameAndProvider(
        string id, string name, string driverClass, string staleMaker,
        int formerlyMisclassifiedCategory)
    {
        var device = Device(id, name, driverClass, staleMaker, staleMaker);
        Assert.NotNull(PnpKnownVendorIdentity.FromDriver(device));
        var report = Report(id);
        var rows = ReferenceDriverCoverage.Create([device], report);
        Assert.Equal(0, rows.Sum(row => row.Devices));
        Assert.Equal(0, rows.Sum(row => row.WindowsDriverCandidates));
        Assert.Equal(0, rows[formerlyMisclassifiedCategory].Devices);
        Assert.DoesNotContain("PRIVATE",
            ScanCoverageText.Create(report, [], 1, 0, 0,
                [device], inventoryComplete: true));
    }

    [Fact]
    public void ConflictingPrimaryVendorsDoNotInventNvidiaOrIntelCategory()
    {
        var device = Device(NvidiaGpu,
            "NVIDIA GeForce RTX 4060 Laptop GPU", "Display",
            "NVIDIA") with
        {
            HardwareIds = [NvidiaGpu, RealtekAudio]
        };
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Conflicting,
            PnpKnownVendorIdentity.FromDriver(device));
        var rows = ReferenceDriverCoverage.Create([device], Report(NvidiaGpu));
        Assert.Equal(0, rows.Sum(row => row.Devices));
        Assert.Equal(0, rows.Sum(row => row.WindowsDriverCandidates));
    }

    [Theory]
    [InlineData(NvidiaGpu, "Microsoft Basic Display Adapter", "Display", 5)]
    [InlineData(NvidiaAudio, "High Definition Audio Device", "MEDIA", 6)]
    [InlineData(NvidiaSwc, "Software Component", "SoftwareComponent", 7)]
    [InlineData(@"ACPI\NVDA0820", "System controller", "System", 7)]
    [InlineData(@"PCI\VEN_8086&DEV_46A6", "Microsoft Basic Display Adapter",
        "Display", 0)]
    [InlineData(@"USB\VID_8087&PID_0033", "Generic Bluetooth", "Bluetooth", 1)]
    public void RealNvidiaAndIntelDeviceModelsKeepTheirExistingQaCategories(
        string model, string name, string driverClass, int expectedCategory)
    {
        var device = Device(model, name, driverClass);
        var rows = ReferenceDriverCoverage.Create([device], Report(model));
        Assert.Equal(1, rows.Sum(row => row.Devices));
        Assert.Equal(1, rows[expectedCategory].Devices);
        Assert.Equal(1, rows[expectedCategory].WindowsDriverCandidates);
        Assert.Equal(1, rows.Sum(row => row.WindowsDriverCandidates));
    }

    [Fact]
    public void KillerAx1675iRemainsVisibleWithGenericMicrosoftMetadata()
    {
        const string killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
        var device = Device(killer, "Network Controller", "Net");
        var rows = ReferenceDriverCoverage.Create([device], Report(killer));
        Assert.Equal(1, rows[4].Devices);
        Assert.Equal(1, rows[4].WindowsDriverCandidates);
    }

    [Fact]
    public void UnsupportedProviderHintCannotCreateInstallableGuid()
    {
        var device = Device(RealtekAudio, "NVIDIA Audio", "MEDIA", "NVIDIA");
        var external = new DriverUpdateInfo(
            GuidOne, "NVIDIA Audio update", null, "MEDIA", "NVIDIA",
            null, null, false, "NVIDIA", null, RealtekAudio, null,
            SearchSource: "Unverified vendor mirror");
        Assert.Empty(WuaDiagnosticAssociations.Create([external]));
        Assert.Empty(DeviceWuaMatchGroups.Create(device, [external]).Ordinary);
        Assert.Equal(0, ReferenceDriverCoverage.Create(
            [device], new DriverSearchReport([external],
                [new DriverSearchPassStatus(
                    "Windows Update online", "Completed", 0, "OK")]))
            .Sum(row => row.WindowsDriverCandidates));
        Assert.False(QuickUpdatePolicy.IsEligible(external));
    }
}
