using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DeviceWuaPreparedIndexTests
{
    private const string Nvidia =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaGeneric = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Realtek =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Bluetooth = @"USB\VID_8087&PID_0033";

    private static DriverInfo Device(string name, string id,
        IReadOnlyList<string>? compatible = null) =>
        new(name, "Generic Windows device", "Microsoft", "System",
            "Microsoft", "1.0", null, "oem1.inf", true, true, 0,
            [id], compatible ?? []);

    private static DriverUpdateInfo Offer(int ordinal, string primary,
        IReadOnlyList<string>? additional = null,
        bool advanced = false, string source = "Windows Update online") =>
        new($"00000000-0000-4000-8000-{ordinal:D12}",
            "Generic Windows driver", null, "System", "Microsoft",
            null, null, false, "Microsoft", null, primary, null,
            IsAdvancedCandidate: advanced, SearchSource: source,
            AdditionalHardwareIds: additional);

    [Fact]
    public void FiveHundredDevicesAndTwoHundredMultiIdOffersRemainCorrelated()
    {
        var irrelevant = Enumerable.Range(1, 196).Select(i =>
            Offer(i, $@"PCI\VEN_9999&DEV_{i:X4}",
                Enumerable.Range(1, 256)
                    .Select(n => $@"PCI\VEN_9999&DEV_{i:X4}&SUBSYS_{n:X8}")
                    .ToArray()));
        var offers = irrelevant.Concat([
            Offer(197, Nvidia),
            Offer(198, Killer),
            Offer(199, Realtek),
            Offer(200, Bluetooth)
        ]).ToArray();
        var devices = Enumerable.Range(1, 496)
            .Select(i => Device($"PRIVATE_DEVICE_{i}",
                $@"PCI\VEN_7777&DEV_{i:X4}"))
            .Concat([
                Device("PRIVATE_NVIDIA", Nvidia),
                Device("PRIVATE_KILLER", Killer),
                Device("PRIVATE_REALTEK", Realtek),
                Device("PRIVATE_BLUETOOTH", Bluetooth)
            ]).ToArray();

        Assert.Equal(200, offers.Length);
        Assert.Equal(500, devices.Length);
        var prepared = DeviceWuaMatchGroups.Prepare(offers);
        Assert.True(prepared.UsesPnpIndex);
        Assert.Equal(200, prepared.UniqueGuidCount);
        Assert.All(devices.Take(496), device =>
            Assert.Equal(0, prepared.ForDevice(device).TotalCorrelated));
        for (var i = 0; i < 4; i++)
        {
            var result = prepared.ForDevice(devices[496 + i]);
            Assert.Equal(offers[196 + i].Id,
                Assert.Single(result.Ordinary).Id);
            Assert.Empty(result.ReviewOnly);
            Assert.Empty(result.ModelOnlyReview!);
        }
    }

    [Fact]
    public void IndexedMatchingPreservesHardwareBeforeCompatibleAndNormalizesIds()
    {
        var device = Device("PRIVATE_GPU", Nvidia,
            [NvidiaGeneric]);
        var exact = Offer(1, "  " + Nvidia.ToLowerInvariant() + "  ");
        var compatible = Offer(2, NvidiaGeneric);
        var nearMiss = Offer(3, @"PCI\VEN_10DE0&DEV_28A0");
        var prepared = DeviceWuaMatchGroups.Prepare(
            [compatible, nearMiss, exact]);
        Assert.True(prepared.UsesPnpIndex);
        var matches = prepared.ForDevice(device);
        Assert.Equal([exact.Id, compatible.Id],
            matches.Ordinary.Select(update => update.Id));
        Assert.Empty(matches.ModelOnlyReview!);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(device,
            matches.Ordinary[0]));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(device,
            matches.Ordinary[1]));
    }

    [Fact]
    public void ModelOnlyHintsRemainReviewOnlyEvenForOrdinaryWuaMetadata()
    {
        var wua = Offer(1, NvidiaGeneric);
        var unknown = Offer(2, NvidiaGeneric,
            source: "Untrusted OEM website");
        var prepared = DeviceWuaMatchGroups.Prepare([wua, unknown]);
        var group = prepared.ForDevice(Device("PRIVATE_GPU", Nvidia));
        Assert.True(prepared.UsesPnpIndex);
        Assert.Empty(group.Ordinary);
        Assert.Empty(group.ReviewOnly);
        Assert.Equal(wua.Id, Assert.Single(group.ModelOnlyReview!).Id);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(group.Ordinary));
    }

    [Fact]
    public void SecondaryWuaIdsAreDeduplicatedAndAdvancedIsNotAutoSelected()
    {
        var advanced = Offer(1, @"PCI\VEN_9999&DEV_1111",
            [Bluetooth, Bluetooth.ToLowerInvariant(), "  " + Bluetooth + "  "],
            advanced: true, source: "Windows Update online · avanzato");
        var prepared = DeviceWuaMatchGroups.Prepare([advanced, advanced]);
        var group = prepared.ForDevice(Device("PRIVATE_BLUETOOTH", Bluetooth));
        Assert.True(prepared.UsesPnpIndex);
        Assert.Empty(group.Ordinary);
        Assert.Single(group.ReviewOnly);
        Assert.Empty(group.ModelOnlyReview!);
        Assert.Equal(1, group.TotalCorrelated);
    }

    [Fact]
    public void CandidateAfterTransportCapCannotBypassMatcherThroughIndex()
    {
        var ids = Enumerable.Range(1, 1024)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}")
            .Append(@"PCI\VEN_9999&DEV_FFFF")
            .ToArray();
        var offer = Offer(1, @"PCI\VEN_9999&DEV_0000", ids);
        var prepared = DeviceWuaMatchGroups.Prepare([offer]);
        Assert.True(prepared.UsesPnpIndex);
        Assert.Equal(0, prepared.ForDevice(
            Device("PRIVATE_TAIL", @"PCI\VEN_9999&DEV_FFFF"))
            .TotalCorrelated);
    }
    [Fact]
    public void PciClassOnlyWuaGuidCannotBecomeOrdinaryUpdateOrModelHint()
    {
        const string generic = @"PCI\CC_030000";
        var device = Device("PRIVATE_NVIDIA",
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA", [generic]);
        var unrelated = Offer(1, generic);
        var valid = Offer(2, device.HardwareIds[0]);
        var prepared = DeviceWuaMatchGroups.Prepare([unrelated, valid]);

        Assert.True(prepared.UsesPnpIndex);
        var result = prepared.ForDevice(device);
        Assert.Equal(valid.Id, Assert.Single(result.Ordinary).Id);
        Assert.Empty(result.ReviewOnly);
        Assert.Empty(result.ModelOnlyReview!);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(
            prepared.ForDevice(Device("PRIVATE_CLASS_ONLY", generic))
                .Ordinary));
    }

}
