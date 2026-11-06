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
        Assert.Same(candidate, Assert.Single(groups.ReviewOnly));
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

        Assert.Same(ordinary, Assert.Single(groups.Ordinary));
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
