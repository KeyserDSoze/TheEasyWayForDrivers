using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Home NVIDIA/Intel/Realtek cards and the PnPUtil opt-in device selector
/// must not disagree with validated non-PCI vendor models. A copied vendor
/// report must preserve only canonical public labels (never PnP instances).
/// </summary>
public sealed class HomeCrossBusVendorPrecedenceTests
{
    private const string NvidiaGpu = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaAudio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string NvidiaSwc = @"SWC\VEN_10DE&DEV_28A0";
    private const string RealtekAudio = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string RealtekIntelAudio =
        @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string RealtekSwc = @"SWC\VEN_10EC&DEV_0295";
    private const string QualcommUsb = @"USB\VID_0CF3&PID_E300";
    private const string RealtekUsb = @"USB\VID_0BDA&PID_8771";
    private const string NvidiaUsb = @"USB\VID_0955&PID_7019";

    private static DriverInfo Device(string privateInstance, string hardware,
        string pnpClass = "MEDIA", string name = "NVIDIA Audio") =>
        new(privateInstance, name, "NVIDIA", pnpClass, "NVIDIA", "1.0",
            null, "PRIVATE_OEM_INF", true, true, 0, [hardware], []);

    private static DriverSearchReport Search(params string[] hardwareIds) =>
        new(hardwareIds.Select(id => new DriverUpdateInfo(
                Guid.NewGuid().ToString("D"), "Windows driver",
                null, "MEDIA", "Microsoft", null, null, false,
                "Microsoft", null, id, null,
                SearchSource: "Windows Update online"))
                .ToArray(),
            [new DriverSearchPassStatus(
                "Windows Update online", "Completed",
                hardwareIds.Length, "PRIVATE_COM_DETAILS")]);

    [Fact]
    public void RealtekHdaudioWithOldNvidiaProviderCannotEnterNvidiaHome()
    {
        var codec = Device("PRIVATE_CODEC", RealtekAudio);
        var gpu = Device("PRIVATE_GPU", NvidiaGpu,
            "Display", "Microsoft Basic Display Adapter") with
        {
            Manufacturer = "Microsoft", DriverProvider = "Microsoft"
        };
        var report = Search(RealtekAudio, NvidiaGpu);
        var home = HomeVendorCoverageSummary.Create([codec, gpu], report, true);

        Assert.Equal(3, home.Count);
        Assert.Equal("1 dispositivo PnP classificato",
            home[0].DeviceStatus);
        Assert.Contains("1 candidato WUA per ID", home[0].WindowsStatus);
        Assert.Equal("0 dispositivi PnP classificati",
            home[1].DeviceStatus);
        Assert.Equal("1 dispositivo PnP classificato",
            home[2].DeviceStatus);
        Assert.Contains("1 candidato WUA per ID", home[2].WindowsStatus);

        Assert.Equal(gpu.DeviceId,
            HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                [codec, gpu], "NVIDIA", true)?.DeviceId);
        Assert.Equal(codec.DeviceId,
            HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                [codec, gpu], "Realtek", true)?.DeviceId);
        Assert.All(home, row => Assert.False(row.IsInstallable));
        Assert.DoesNotContain("PRIVATE", string.Join(" ",
            home.Select(row => row.DeviceStatus + row.WindowsStatus)));
        var eight = ReferenceDriverCoverage.Create([codec, gpu], report);
        Assert.Equal(1, eight[5].Devices);
        Assert.Equal(0, eight[6].Devices);
        Assert.Equal(1, eight[5].WindowsDriverCandidates);
    }

    [Theory]
    [InlineData(RealtekAudio, "MEDIA")]
    [InlineData(RealtekIntelAudio, "MEDIA")]
    [InlineData(RealtekSwc, "SoftwareComponent")]
    [InlineData(RealtekUsb, "Net")]
    [InlineData(QualcommUsb, "Bluetooth")]
    [InlineData(@"USB\VID_0A5C&PID_216F", "Bluetooth")]
    [InlineData(@"USB\VID_0E8D&PID_0003", "Bluetooth")]
    public void ValidatedForeignVendorBlocksNvidiaCardAndLocalStoreSelection(
        string hardware, string pnpClass)
    {
        var stale = Device("PRIVATE_FOREIGN_INSTANCE", hardware,
            pnpClass, "NVIDIA GeForce RTX 4060");
        var known = PnpKnownVendorIdentity.FromDriver(stale);
        Assert.NotNull(known);
        Assert.NotEqual(PnpKnownVendorIdentity.Vendor.Nvidia, known);
        Assert.True(NvidiaHardwareDetector.IsNvidiaDevice(stale));

        var cards = HomeVendorCoverageSummary.Create(
            [stale], Search(hardware), true);
        Assert.Equal("0 dispositivi PnP classificati",
            cards[0].DeviceStatus);
        Assert.False(cards[0].CanInspectLocalDrivers);
        Assert.Contains("0 candidati WUA", cards[0].WindowsStatus);
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [stale], "NVIDIA", true));
        Assert.DoesNotContain("PRIVATE", cards[0].WindowsStatus);
    }

    [Fact]
    public void ConflictingNonPciModelVendorsFailClosedForHomeAndLocalClick()
    {
        var contradictory = Device("PRIVATE_CONFLICT", RealtekAudio) with
        {
            HardwareIds = [RealtekAudio, NvidiaAudio]
        };
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Conflicting,
            PnpKnownVendorIdentity.FromDriver(contradictory));
        var cards = HomeVendorCoverageSummary.Create(
            [contradictory], Search(RealtekAudio, NvidiaAudio), true);
        Assert.All(cards, card =>
            Assert.Equal("0 dispositivi PnP classificati", card.DeviceStatus));
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [contradictory], "NVIDIA", true));
        Assert.Null(HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
            [contradictory], "Realtek", true));
    }

    [Theory]
    [InlineData(NvidiaGpu, "Display")]
    [InlineData(NvidiaAudio, "MEDIA")]
    [InlineData(NvidiaSwc, "SoftwareComponent")]
    [InlineData(@"ACPI\NVDA0820", "System")]
    [InlineData(NvidiaUsb, "USB")]
    public void GenuineNvidiaHardwareRetainsHomeCountAndReadOnlySelector(
        string hardware, string pnpClass)
    {
        var device = Device("PRIVATE_REAL_NVIDIA", hardware,
            pnpClass, "Generic Windows Device") with
        {
            Manufacturer = "Microsoft", DriverProvider = "Microsoft"
        };
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Nvidia,
            PnpKnownVendorIdentity.FromDriver(device));
        var cards = HomeVendorCoverageSummary.Create(
            [device], Search(hardware), true);
        Assert.Equal("1 dispositivo PnP classificato",
            cards[0].DeviceStatus);
        Assert.Contains("1 candidato WUA per ID", cards[0].WindowsStatus);
        Assert.True(cards[0].CanInspectLocalDrivers);
        Assert.False(cards[0].IsInstallable);
        Assert.Same(device,
            HomeVendorCoverageSummary.ChooseLocalDriverStoreDevice(
                [device], "NVIDIA", true));
        Assert.Equal("0 dispositivi PnP classificati",
            cards[2].DeviceStatus);
    }

    [Fact]
    public void UnrecognizedPrimaryVendorRetainsPriorLegacyHomeFallback()
    {
        var unknown = Device("PRIVATE_UNKNOWN", @"ROOT\NONSTANDARD",
            "System", "NVIDIA System Controller");
        Assert.Null(PnpKnownVendorIdentity.FromDriver(unknown));
        var cards = HomeVendorCoverageSummary.Create([unknown], null, true);
        Assert.Equal("1 dispositivo PnP classificato",
            cards[0].DeviceStatus);
        Assert.Contains("non verificata", cards[0].WindowsStatus);
        Assert.True(cards[0].CanInspectLocalDrivers);
        Assert.False(cards[0].IsInstallable);
    }

    [Fact]
    public void AnonymousVendorExportPreservesRecognizedLogitechRazerLabels()
    {
        var logitech = new DriverInfo("PRIVATE_LOGI_INSTANCE",
            "PRIVATE_LOGI_DEVICE_NAME", "Microsoft", "HIDClass",
            "Microsoft", "1.0", null, "PRIVATE_OEM_INF",
            true, true, 0, [@"HID\VID_046D&PID_C534"], []);
        var razer = new DriverInfo("PRIVATE_RAZER_INSTANCE",
            "PRIVATE_RAZER_DEVICE_NAME", "Microsoft", "USB",
            "Microsoft", "1.0", null, "PRIVATE_OEM_INF",
            true, true, 0, [@"USB\VID_1532&PID_0100"], []);
        var coverage = VendorInventoryCoverage.Create(
            [logitech, razer], []);
        Assert.Equal(2, coverage.Rows.Count);
        var report = Search();
        var copied = ScanCoverageText.Create(report, coverage.Rows,
            2, 0, 0, [logitech, razer], true,
            vendorCoverage: coverage);

        Assert.Contains("Logitech: 1 dispositivi", copied);
        Assert.Contains("Razer: 1 dispositivi", copied);
        Assert.DoesNotContain("altro produttore: 1 dispositivi", copied);
        Assert.DoesNotContain("PRIVATE", copied);
        Assert.DoesNotContain("VID_046D", copied);
        Assert.DoesNotContain("VID_1532", copied);

        // Only exact fixed labels may be exported, even if the caller
        // provides a short but identifying source/vendor string.
        var injected = ScanCoverageText.Create(report,
        [
            new VendorCoverageRow("loGiTech", 2, 0, 0, 0),
            new VendorCoverageRow("rAzEr", 3, 0, 0, 0),
            new VendorCoverageRow("Logitech PRIVATE_SERIAL", 4, 0, 0, 0)
        ], 9, 0, 0);
        Assert.Contains("Logitech: 2 dispositivi", injected);
        Assert.Contains("Razer: 3 dispositivi", injected);
        Assert.Contains("altro produttore: 4 dispositivi", injected);
        Assert.DoesNotContain("PRIVATE", injected);
        Assert.DoesNotContain("loGiTech", injected);
        Assert.DoesNotContain("rAzEr", injected);
    }

    [Fact]
    public void FailedWuaPassStillCannotClaimZeroNvidiaOffers()
    {
        var codec = Device("PRIVATE_CODEC", RealtekAudio);
        var failed = new DriverSearchReport(
            [], [new DriverSearchPassStatus("Windows Update online",
                "Failed", 0, "PRIVATE_PROXY")]);
        var cards = HomeVendorCoverageSummary.Create([codec], failed, true);
        Assert.Equal("0 dispositivi PnP classificati",
            cards[0].DeviceStatus);
        Assert.Contains("non verificata", cards[0].WindowsStatus);
        Assert.DoesNotContain("0 candidati", cards[0].WindowsStatus);
        Assert.Equal("1 dispositivo PnP classificato",
            cards[2].DeviceStatus);
        Assert.False(cards[2].IsInstallable);
    }
}
