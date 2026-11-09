using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>Regressions for the Home eight-component association index.
/// The count is diagnostic only and must not manufacture installable offers.</summary>
public sealed class ReferenceDriverCoverageIndexedTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Audio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string Platform = @"SWC\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Bluetooth = @"USB\VID_8087&PID_0033";

    private static DriverInfo Device(string instance, string id,
        string label, string type, IReadOnlyList<string>? compatible = null) =>
        new(instance, label, "Microsoft", type, "Microsoft",
            "1.0.0.0", null, "oem3.inf", true, true, 0,
            [id], compatible ?? []);

    private static DriverUpdateInfo Offer(int ordinal, string id,
        string? guid = null, IReadOnlyList<string>? extras = null) =>
        new(guid ?? $"00000000-0000-4000-8000-{ordinal:D12}",
            "Generic Windows driver", null, "System", "Microsoft",
            null, null, false, "Microsoft", "Generic Windows",
            id, null, SearchSource: "Windows Update online",
            AdditionalHardwareIds: extras);

    private static DriverSearchReport Report(
        IReadOnlyList<DriverUpdateInfo> offers,
        IReadOnlyList<WuaDriverRelatedSoftware>? software = null) =>
        new(offers, [new("Windows Update online",
            "Completed", offers.Count, "Query completed")],
            RelatedSoftware: software);

    [Fact]
    public void FiveHundredDevicesWithTwoHundredOffersStillCorrelateAllEightCategories()
    {
        // This data formerly executed 500 x 200 x (up to 1024)
        // comparisons on every Home/report refresh.
        var genericIntel = Enumerable.Range(1, 495)
            .Select(i => Device($"PRIVATE_INTEL_{i}",
                $@"PCI\VEN_8086&DEV_{i:X4}",
                "Generic Microsoft Display", "Display"));
        var gpu = Device("PRIVATE_GPU", Gpu, "Microsoft Basic Display", "Display");
        var hdAudio = Device("PRIVATE_AUDIO", Audio, "High Definition Audio Device", "MEDIA");
        var swc = Device("PRIVATE_PLATFORM", Platform, "Software Component", "SoftwareComponent");
        var killer = Device("PRIVATE_KILLER", Killer, "Network Controller", "Net");
        var bluetooth = Device("PRIVATE_BT", Bluetooth, "Bluetooth Radio", "Bluetooth");
        var devices = genericIntel.Concat([gpu, hdAudio, swc, killer, bluetooth]).ToArray();
        Assert.Equal(500, devices.Length);

        var unrelated = Enumerable.Range(1, 195)
            .Select(i => Offer(i, $@"PCI\VEN_9999&DEV_{i:X4}"));
        const string sharedGuid = "00000000-0000-4000-8000-000000009999";
        var offers = unrelated.Concat([
            Offer(196, Gpu, sharedGuid),
            Offer(197, Audio, sharedGuid), // one WUA GUID, two devices
            Offer(198, Killer),
            Offer(199, Bluetooth),
            Offer(200, Platform)
        ]).ToArray();
        Assert.Equal(200, offers.Length);

        var results = ReferenceDriverCoverage.Create(devices, Report(offers));
        Assert.Equal(8, results.Count);
        Assert.Equal(495, results[0].Devices);
        Assert.Equal(1, results[1].Devices);
        Assert.Equal(1, results[4].Devices);
        Assert.Equal(1, results[5].Devices);
        Assert.Equal(1, results[6].Devices);
        Assert.Equal(1, results[7].Devices);
        Assert.All(new[] { 1, 4, 5, 6, 7 },
            index => Assert.Equal(1, results[index].WindowsDriverCandidates));
        Assert.Equal(0, results[0].WindowsDriverCandidates);
        Assert.Equal(0, results[2].WindowsDriverCandidates);
        Assert.Equal(0, results[3].WindowsDriverCandidates);
        Assert.Equal(0, results.Sum(row => row.SoftwareTitleHints));
        Assert.Equal(5, results.Sum(row => row.WindowsDriverCandidates));
    }

    [Fact]
    public void IndexRecognizesCompatibleIdsCaseAndWhitespaceButNotNearMissModels()
    {
        var gpu = Device("PRIVATE_GPU", Gpu, "Generic Device", "Display",
            [@"PCI\VEN_10DE&DEV_28A0"]);
        var bt = Device("PRIVATE_BT", Bluetooth, "Bluetooth Radio", "Bluetooth");
        var unrelated = Device("PRIVATE_OTHER",
            @"PCI\VEN_10DE0&DEV_28A0", "Generic Device", "System");
        var offers = new[]
        {
            Offer(1, @"pci\ven_10de&dev_28a0  "),
            Offer(2, @"usb\vid_8087&pid_0033"),
            Offer(3, @"PCI\VEN_10DE0&DEV_28A0"),
            Offer(4, @"PCI\VEN_10DE&DEV_28A0&SUBSYS_99999999")
        };
        var result = ReferenceDriverCoverage.Create(
            [gpu, bt, unrelated], Report(offers));
        Assert.Equal(1, result[5].Devices);
        Assert.Equal(1, result[5].WindowsDriverCandidates);
        Assert.Equal(1, result[1].WindowsDriverCandidates);
        Assert.Equal(2, result.Sum(row => row.WindowsDriverCandidates));
    }

    [Fact]
    public void OneDeclaredIdCanCorrelateTwoCategoriesWithoutTwoUpdateGuids()
    {
        var gpu = Device("PRIVATE_GPU", Gpu, "Microsoft Basic Display", "Display");
        var syntheticAudio = Device("PRIVATE_HD_AUDIO", Gpu, "NVIDIA Audio", "MEDIA");
        var update = Offer(1, Gpu);
        var results = ReferenceDriverCoverage.Create(
            [gpu, syntheticAudio], Report([update, update]));

        Assert.Equal(1, results[5].WindowsDriverCandidates);
        Assert.Equal(1, results[6].WindowsDriverCandidates);
        Assert.Equal(2, results.Sum(row => row.WindowsDriverCandidates));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([update]));
    }

    [Fact]
    public void IpcCapPreventsExtraId1025AndSoftwareTitleNeverBecomesDriver()
    {
        var gpu = Device("PRIVATE_GPU", Gpu, "Microsoft Basic Display", "Display");
        var first1024 = Enumerable.Range(1, WuaDriverEntryLimits.MaximumAdditionalIds)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .ToArray();
        var beyondCap = Offer(1, @"PCI\VEN_9999&DEV_0000",
            extras: first1024.Append(Gpu).ToArray());
        var report = Report([beyondCap], [
            new WuaDriverRelatedSoftware(
                "00000000-0000-4000-8000-000000000777",
                "NVIDIA Platform Controllers and Framework",
                "Windows Update online · componenti software")
        ]);
        var result = ReferenceDriverCoverage.Create([gpu], report);
        Assert.Equal(0, result[5].WindowsDriverCandidates);
        Assert.Equal(0, result[7].WindowsDriverCandidates);
        Assert.Equal(1, result[7].SoftwareTitleHints);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds([]));

        var copy = ScanCoverageText.Create(report, [], 1, 0, 0,
            [gpu], inventoryComplete: true);
        Assert.DoesNotContain("PRIVATE_GPU", copy);
        Assert.DoesNotContain(Gpu, copy);
        Assert.DoesNotContain(beyondCap.Id, copy);
    }

    [Fact]
    public void DuplicateDeviceInstanceAndSameGuidDifferentSourcesAreNotDoubleCounted()
    {
        var gpu = Device("PRIVATE_GPU", Gpu, "Microsoft Basic Display", "Display");
        var first = Offer(1, @"PCI\VEN_9999&DEV_0001");
        var second = first with
        {
            HardwareId = Gpu,
            SearchSource = "Microsoft Update online",
            Id = first.Id.ToUpperInvariant()
        };
        var rows = ReferenceDriverCoverage.Create(
            [gpu, gpu with { DeviceId = "private_gpu" }],
            Report([first, second, second]));
        Assert.Equal(1, rows[5].Devices);
        Assert.Equal(1, rows[5].WindowsDriverCandidates);
    }
}
