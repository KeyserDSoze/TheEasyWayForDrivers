using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #493: vendor support handoff must use the same indexed WUA Type=Driver
/// correlation as device details, never weak model inference or OEM links.
/// </summary>
public sealed class VendorSourceIndexedCorrelationTests
{
    private const string NvidiaGpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaAudio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(int n, string id, string cls = "Net",
        string name = "Network Controller") =>
        new("PRIVATE_INSTANCE_" + n, name, "Microsoft", cls,
            "Microsoft", "1.0.0.0", null, "oem17.inf",
            true, true, 0, [id], []);

    private static DriverUpdateInfo Offer(int n, string primary,
        IReadOnlyList<string>? additional = null,
        string source = "Windows Update online",
        bool advanced = false,
        bool optional = false) =>
        new($"00000000-0000-4000-8000-{n:D12}",
            "Synthetic Windows driver", null, "System", "Microsoft",
            null, null, false, "Microsoft", null, primary, null,
            IsAdvancedCandidate: advanced, IsOptional: optional,
            SearchSource: source, AdditionalHardwareIds: additional);

    [Fact]
    public void ExactAndCompatibleIdsCountOneGuidNotReviewOrInstallOffers()
    {
        var gpu = Device(1, NvidiaGpu, "Display",
            "Microsoft Basic Display Adapter");
        var audio = Device(2, NvidiaAudio, "MEDIA",
            "High Definition Audio Device");
        var killer = Device(3, Killer, "Net", "Killer Wi-Fi 6E");
        var realtek = Device(4, Realtek, "MEDIA", "High Definition Audio Device")
            with { HardwareIds = [@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0294"],
                   CompatibleIds = [Realtek] };
        var rows = VendorSourceDiscovery.Find(
            [gpu, audio, killer, realtek],
            [
                Offer(1, NvidiaGpu),
                Offer(2, NvidiaAudio,
                    advanced: true,
                    source: "Windows Update online · avanzato"),
                Offer(3, Killer, optional: true),
                Offer(4, Realtek)
            ]);
        Assert.Equal(4, rows.Count);
        Assert.All(rows, row => Assert.Equal(1, row.WuaMatches));
        Assert.Contains(rows, row => row.Vendor == "NVIDIA" &&
            row.DeviceName.Contains("Display", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Vendor == "Killer / Intel");
        Assert.Contains(rows, row => row.Vendor == "Realtek");
        Assert.All(rows, row =>
        {
            Assert.False(row.IsVendorApplicable);
            Assert.False(row.IsInstallable);
            Assert.False(row.IsCatalogMetadata);
        });
    }

    [Fact]
    public void SameGuidAcrossWuaSourcesAndFormsRemainsOneAssociation()
    {
        var gpu = Device(1, NvidiaGpu, "Display",
            "Microsoft Basic Display Adapter");
        var audio = Device(2, NvidiaAudio, "MEDIA",
            "High Definition Audio Device");
        var first = Offer(10, NvidiaGpu, source: "Windows configurato");
        var second = first with
        {
            Id = Guid.Parse(first.Id).ToString("B"),
            HardwareId = NvidiaAudio,
            SearchSource = "Microsoft Update online"
        };
        var unknown = Offer(11, Killer,
            source: "PRIVATE_OEM_MIRROR");
        var rows = VendorSourceDiscovery.Find(
            [gpu, audio], [unknown, first, second]);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(1, row.WuaMatches));
        Assert.Equal(2, rows.Sum(row => row.WuaMatches));
        Assert.All(rows, row => Assert.False(row.IsInstallable));
    }

    [Fact]
    public void PublicModelOnlyHintDoesNotInventExactWuaPnpCorrelation()
    {
        var gpu = Device(1, NvidiaGpu, "Display",
            "Microsoft Basic Display Adapter");
        var generic = Offer(1, @"PCI\VEN_10DE&DEV_28A0");
        var source = Assert.Single(
            VendorSourceDiscovery.Find([gpu], [generic]));
        Assert.Equal("NVIDIA", source.Vendor);
        Assert.Equal(0, source.WuaMatches);

        var prepared = DeviceWuaMatchGroups.Prepare([generic]);
        var evidence = prepared.ForDevice(gpu);
        Assert.Empty(evidence.Ordinary);
        Assert.Empty(evidence.ReviewOnly);
        Assert.Single(evidence.ModelOnlyReview!);
        Assert.False(source.IsInstallable);
        Assert.Contains("nessun aggiornamento OEM confermato",
            source.StatusText);
    }

    [Fact]
    public void MalformedAndUnknownWuaHardwareEvidenceDoesNotMatch()
    {
        var gpu = Device(1, NvidiaGpu, "Display",
            "Microsoft Basic Display Adapter");
        var rows = VendorSourceDiscovery.Find([gpu],
        [
            Offer(1, NvidiaGpu + "\t"),
            Offer(2, NvidiaGpu, source: "PRIVATE_DRIVER_CATALOG"),
            Offer(3, @"PCI\CC_0300"),
            Offer(4, @"PCI\VEN_10DE")
        ]);
        var row = Assert.Single(rows);
        Assert.Equal(0, row.WuaMatches);
        Assert.False(row.IsInstallable);
    }

    [Fact]
    public void LargeSyntheticWuaAndPnpInventoriesKeepLateMajorVendorMatches()
    {
        // 496 distinct Intel devices, 104 distinct WUA GUIDs and over
        // 12,000 valid WUA entry IDs. The former nested matcher repeated
        // those IDs for EACH of the 500 PnP devices.
        var intel = Enumerable.Range(1, 496)
            .Select(i => Device(i,
                $@"PCI\VEN_8086&DEV_{i:X4}"))
            .ToArray();
        var gpu = Device(497, NvidiaGpu, "Display",
            "Microsoft Basic Display Adapter");
        var audio = Device(498, NvidiaAudio, "MEDIA",
            "High Definition Audio Device");
        var killer = Device(499, Killer, "Net", "Killer Wi-Fi 6E");
        var realtek = Device(500, Realtek, "MEDIA",
            "High Definition Audio Device");
        var irrelevant = Enumerable.Range(1, 100)
            .Select(i => Offer(i,
                $@"PCI\VEN_9999&DEV_{i:X4}",
                Enumerable.Range(1, 128).Select(j =>
                    $@"PCI\VEN_9999&DEV_{(i * 129 + j):X4}")
                    .ToArray()));
        var confirmed = irrelevant.Concat(
        [
            Offer(101, NvidiaGpu),
            Offer(102, NvidiaAudio),
            Offer(103, Killer),
            Offer(104, Realtek)
        ]).ToArray();
        var snapshot = intel.Concat([gpu, audio, killer, realtek]).ToArray();

        var preview = VendorSourceDiscovery.FindWithCoverage(
            snapshot, confirmed, maxResults: 5);
        Assert.Equal(500, preview.EligibleDeviceCount);
        Assert.Equal(5, preview.Rows.Count);
        Assert.Equal(495, preview.OmittedDeviceCount);
        Assert.Equal(4, preview.Rows.Count(row => row.WuaMatches == 1));
        Assert.Contains(preview.Rows, row => row.Vendor == "Killer / Intel" &&
            row.WuaMatches == 1);
        Assert.Contains(preview.Rows, row => row.Vendor == "Realtek" &&
            row.WuaMatches == 1);
        Assert.Equal(2, preview.Rows.Count(row => row.Vendor == "NVIDIA" &&
            row.WuaMatches == 1));
        Assert.All(preview.Rows, row =>
        {
            Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(
                row.OfficialUrl));
            Assert.False(row.IsInstallable);
        });
        Assert.Contains("PARZIALE", preview.CoverageNotice);
        Assert.DoesNotContain("PRIVATE", preview.CoverageNotice);
    }
}
