using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DeviceWuaMatchGroupsTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_11118086";
    private const string Audio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";

    private static DriverInfo Device(
        string id, string title = "Generic Windows display",
        IReadOnlyList<string>? compatible = null) =>
        new("PRIVATE_SERIAL_DEVICE_INSTANCE", title, "Microsoft", "Display",
            "Microsoft", "31.0.15.6000", null, "oem99.inf",
            true, true, 0, [id], compatible ?? []);

    private static DriverUpdateInfo Update(
        string hardwareId, string title = "NVIDIA - Display",
        bool advanced = false, bool hidden = false,
        string source = "Windows Update online",
        string? guid = null, string deviceClass = "Display") =>
        new(guid ?? Guid.NewGuid().ToString("D"), title,
            null, deviceClass, "Microsoft", null, 1000, false,
            "Microsoft", "Generic model", hardwareId, null,
            IsHidden: hidden, IsAdvancedCandidate: advanced,
            SearchSource: source);

    [Fact]
    public void AdvancedGpuWithExactHardwareIdIsReviewOnlyInsteadOfInvisible()
    {
        var candidate = Update(Gpu, "NVIDIA GeForce RTX 4060",
            advanced: true, source: "Windows Update online · avanzato");
        var groups = DeviceWuaMatchGroups.Create(Device(Gpu), [candidate]);

        Assert.Empty(groups.Ordinary);
        Assert.Equal(candidate.Id, Assert.Single(groups.ReviewOnly).Id);
        Assert.Equal(1, groups.TotalCorrelated);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(Device(Gpu), candidate));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(groups.Ordinary));
        Assert.False(QuickUpdatePolicy.IsEligible(candidate));
    }

    [Theory]
    [InlineData(Gpu)]
    [InlineData(Killer)]
    [InlineData(Audio)]
    public void AdvancedNvidiaIntelKillerAndAudioMatchOnlyTheirActualPnpIds(string id)
    {
        var candidate = Update(id, advanced: true,
            source: "Microsoft Update online · avanzato");
        var groups = DeviceWuaMatchGroups.Create(Device(id), [candidate]);

        Assert.Empty(groups.Ordinary);
        Assert.Single(groups.ReviewOnly);
        Assert.Equal(0, DeviceWuaMatchGroups.Create(
            Device(@"PCI\VEN_1234&DEV_0001"), [candidate]).TotalCorrelated);
    }

    [Fact]
    public void OrdinaryAndAdvancedAreShownSeparatelyForSameDevice()
    {
        var ordinary = Update(Gpu, "Ordinary WUA", source: "Windows Update online");
        var advanced = Update(Gpu, "Advanced WUA", advanced: true,
            source: "Windows Update online · avanzato");
        var hidden = Update(Gpu, "Hidden WUA", hidden: true);

        var groups = DeviceWuaMatchGroups.Create(Device(Gpu),
            [advanced, ordinary, hidden]);

        Assert.Equal(ordinary.Id, Assert.Single(groups.Ordinary).Id);
        Assert.Equal(2, groups.ReviewOnly.Count);
        Assert.Equal(3, groups.TotalCorrelated);
        Assert.Equal([ordinary.Id],
            QuickUpdatePolicy.AllOrdinaryIds(groups.Ordinary));
    }

    [Fact]
    public void HiddenOnlyCandidateRemainsNonInstallableEvenIfNotAdvanced()
    {
        var hidden = Update(Killer, hidden: true);
        var groups = DeviceWuaMatchGroups.Create(Device(Killer), [hidden]);
        Assert.Empty(groups.Ordinary);
        Assert.Single(groups.ReviewOnly);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(groups.Ordinary));
    }

    [Fact]
    public void BiosOrFirmwareMetadataCannotEnablePerDeviceInstallation()
    {
        var firmware = Update(Gpu, "NVIDIA firmware package", deviceClass: "Firmware");
        var groups = DeviceWuaMatchGroups.Create(Device(Gpu), [firmware]);
        Assert.Empty(groups.Ordinary);
        Assert.Single(groups.ReviewOnly);
        Assert.False(QuickUpdatePolicy.IsEligible(firmware));
    }

    [Fact]
    public void UnknownSourceWithSamePnpIdCannotBecomeOneClickInstallable()
    {
        var other = Update(Gpu, source: "Untrusted OEM website");
        var groups = DeviceWuaMatchGroups.Create(Device(Gpu), [other]);
        Assert.Empty(groups.Ordinary);
        Assert.Single(groups.ReviewOnly);
    }

    [Fact]
    public void SameWuaGuidAcrossOrdinaryAndAdvancedIsCountedOnlyOnce()
    {
        var id = Guid.NewGuid().ToString("D");
        var ordinary = Update(Gpu, "Ordinary", guid: id);
        var advanced = Update(Gpu, "Advanced", advanced: true,
            source: "Windows Update online · avanzato",
            guid: id.ToUpperInvariant());
        var result = DeviceWuaMatchGroups.Create(Device(Gpu),
            [advanced, ordinary]);

        Assert.Single(result.Ordinary);
        Assert.Empty(result.ReviewOnly);
        Assert.Equal(1, result.TotalCorrelated);
        Assert.Equal([id], QuickUpdatePolicy.AllOrdinaryIds(result.Ordinary));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HiddenPassCannotMaskOrdinaryWuaForSameDeviceGuid(
        bool hiddenFirst)
    {
        const string guid = "00000000-0000-4000-8000-000000000283";
        var hidden = Update(Gpu, "Hidden Windows driver",
            hidden: true, guid: guid);
        var ordinary = Update(Gpu, "Ordinary Windows driver",
            source: "Microsoft Update online", guid: guid);
        var passes = hiddenFirst
            ? new[] { hidden, ordinary } : [ordinary, hidden];

        var result = DeviceWuaMatchGroups.Prepare(passes)
            .ForDevice(Device(Gpu));
        Assert.Equal("Ordinary Windows driver",
            Assert.Single(result.Ordinary).Title);
        Assert.Empty(result.ReviewOnly);
        Assert.Empty(result.ModelOnlyReview!);
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(result.Ordinary));
    }

    [Fact]
    public void CompatibleIdIsCorrelatedButDoesNotClaimExactHardwareStrength()
    {
        var candidate = Update(@"PCI\VEN_8086&DEV_51F1",
            advanced: true, source: "Windows Update online · avanzato");
        var actual = Device(Killer, "Generic network controller",
            [@"PCI\VEN_8086&DEV_51F1"]);

        var result = DeviceWuaMatchGroups.Create(actual, [candidate]);
        Assert.Empty(result.Ordinary);
        Assert.Single(result.ReviewOnly);
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(actual, candidate));
    }

    [Fact]
    public void VendorNameAndSimilarIdsCannotFabricateADeviceMatch()
    {
        var candidate = Update(Gpu, "NVIDIA RTX");
        var wrong = Device(@"PCI\VEN_10DE0&DEV_28A0&SUBSYS_16A217AA",
            "NVIDIA RTX 4060 Laptop");
        var other = Device(@"PCI\VEN_10DE&DEV_28A1&SUBSYS_16A217AA",
            "NVIDIA GeForce RTX 4060");
        Assert.Equal(0, DeviceWuaMatchGroups.Create(wrong, [candidate]).TotalCorrelated);
        Assert.Equal(0, DeviceWuaMatchGroups.Create(other, [candidate]).TotalCorrelated);
    }

    [Fact]
    public void InvalidGuidsAreNotConfirmedWuaDriverOffers()
    {
        var invalid = Update(Gpu, guid: "fake-offer-id");
        var result = DeviceWuaMatchGroups.Create(Device(Gpu), [invalid]);
        Assert.Empty(result.Ordinary);
        Assert.Empty(result.ReviewOnly);
    }


    [Fact]
    public void SameGuidFromOrdinaryAndAdvancedWuaSourcesConnectsAllDeviceIds()
    {
        const string guid = "00000000-0000-4000-8000-000000000278";
        const string intel = @"PCI\VEN_8086&DEV_46A6";
        const string nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
        const string killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
        var ordinary = Update(intel, "Ordinary WUA", guid: guid);
        var advanced = Update(nvidia, "Advanced WUA", advanced: true,
            source: "Microsoft Update online · avanzato",
            guid: guid.ToUpperInvariant()) with
        {
            AdditionalHardwareIds = [killer]
        };
        var prepared = DeviceWuaMatchGroups.Prepare([advanced, ordinary]);

        Assert.Equal(1, prepared.UniqueGuidCount);
        foreach (var id in new[] { intel, nvidia, killer })
        {
            var group = prepared.ForDevice(Device(id));
            var offer = Assert.Single(group.Ordinary);
            Assert.Equal(guid, offer.Id);
            Assert.Equal("Ordinary WUA", offer.Title);
            Assert.True(QuickUpdatePolicy.IsEligible(offer));
            Assert.Empty(group.ReviewOnly);
            Assert.Empty(group.ModelOnlyReview!);
        }

        var direct = DeviceWuaMatchGroups.Create(Device(nvidia),
            [advanced, ordinary]);
        Assert.Equal(guid, Assert.Single(direct.Ordinary).Id);
    }

    [Fact]
    public void HiddenAdvancedAndUntrustedOnlyGuidRemainReviewOnly()
    {
        const string guid = "00000000-0000-4000-8000-000000000279";
        var advanced = Update(Gpu, advanced: true,
            source: "Windows configurato · avanzato", guid: guid);
        var hidden = Update(Killer, guid: guid.ToUpperInvariant(), hidden: true);
        var combined = DeviceWuaMatchGroups.Prepare([advanced, hidden]);

        foreach (var id in new[] { Gpu, Killer })
        {
            var group = combined.ForDevice(Device(id));
            Assert.Empty(group.Ordinary);
            Assert.Single(group.ReviewOnly);
            Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(group.Ordinary));
        }

        var untrusted = Update(Gpu, guid: guid, source: "Unknown website");
        var other = DeviceWuaMatchGroups.Prepare([advanced, untrusted])
            .ForDevice(Device(Gpu));
        Assert.Empty(other.Ordinary);
        Assert.Single(other.ReviewOnly);
    }

    [Fact]
    public void SourceMergeKeepsLateRealtekAfterFirstPassFills1024IdCap()
    {
        const string guid = "00000000-0000-4000-8000-000000000280";
        const string realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
        var first = Update(@"PCI\VEN_9999&DEV_0000", guid: guid) with
        {
            AdditionalHardwareIds = Enumerable.Range(1, 1024)
                .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray()
        };
        var second = Update(@"PCI\VEN_8888&DEV_0000",
            guid: guid.ToUpperInvariant(),
            advanced: true,
            source: "Windows Update online · avanzato") with
        {
            AdditionalHardwareIds = [realtek]
        };
        var matched = DeviceWuaMatchGroups.Prepare([first, second])
            .ForDevice(Device(realtek));
        var entry = Assert.Single(matched.Ordinary);
        Assert.Equal(guid, entry.Id);
        Assert.Equal(1024, entry.AdditionalHardwareIds!.Count);
        Assert.Contains(realtek, entry.AdditionalHardwareIds);
        Assert.True(entry.AdditionalHardwareIdsIncomplete);
        Assert.Empty(matched.ReviewOnly);
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(matched.Ordinary));
    }

    [Fact]
    public void MissingGuidOrNearMissCannotTurnAnUpdateIntoAnOffer()
    {
        var missing = Update(Gpu, guid: "fake-GUID");
        var wrong = Update(@"PCI\VEN_10DE0&DEV_28A0");
        var withoutId = Update("");
        var prepared = DeviceWuaMatchGroups.Prepare([missing, wrong, withoutId]);
        Assert.Equal(2, prepared.UniqueGuidCount);
        var group = prepared.ForDevice(Device(Gpu));
        Assert.Empty(group.Ordinary);
        Assert.Empty(group.ReviewOnly);
        Assert.Empty(group.ModelOnlyReview!);
    }

    [Fact]
    public void OptionalOrdinaryWuaOfferRemainsEligibleWithoutAddingNewGuids()
    {
        var optional = Update(Gpu) with { IsOptional = true };
        var result = DeviceWuaMatchGroups.Create(Device(Gpu), [optional]);

        Assert.Single(result.Ordinary);
        Assert.Empty(result.ReviewOnly);
        Assert.True(QuickUpdatePolicy.IsEligible(optional));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds(result.Ordinary));
    }
}
