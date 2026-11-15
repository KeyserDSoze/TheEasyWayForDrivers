using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #463: the transport subset of a single real WUA GUID must not lose
/// manufacturer-recognized audio/SWC models because GetVendorGroup
/// maintains a different bus prefix list from PnPKnownVendorIdentity.
/// The output remains read-only matching evidence, NOT install authority.
/// </summary>
public sealed class WuaCrossBusKnownVendorFairnessTests
{
    private const string NvidiaIntelAudio =
        @"INTELAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string IntelAlternateAudio =
        @"INTELAUDIO\FUNC_01&VEN_1A56&DEV_1234";
    private const string RealtekOtherFunction =
        @"HDAUDIO\FUNC_02&VEN_10EC&DEV_0295";
    private const string AmdIntelAudio =
        @"INTELAUDIO\FUNC_01&VEN_1002&DEV_AA01";
    private const string QualcommSwc =
        @"SWC\VEN_17CB&DEV_1001";
    private const string BroadcomSwc =
        @"SWC\VEN_14E4&DEV_0123";
    private const string MediaTekSwc =
        @"SWC\VEN_14C3&DEV_3210";
    private const string Sndw =
        @"SNDW\CLASS_00&MAN_025D&PART_0711";
    private const string Acp =
        @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2";
    private const string Sdca =
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711";
    private const string Storage =
        @"USBSTOR\DISK&VEN_SANDISK&PROD_ULTRA";
    private const string Logitech =
        @"HID\VID_046D&PID_C534";
    private static readonly string[] KnownCrossBusModels =
    [
        NvidiaIntelAudio, IntelAlternateAudio, RealtekOtherFunction,
        AmdIntelAudio, QualcommSwc, BroadcomSwc, MediaTekSwc
    ];

    private static string[] Common(int count) =>
        Enumerable.Range(1, count)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();

    private static string[] RareManufacturers(int count) =>
        Enumerable.Range(0, count)
            .Select(i => $@"PCI\VEN_{(0x4000 + i):X4}&DEV_0001")
            .ToArray();

    private static DriverInfo Device(string id) =>
        new("PRIVATE_LOCAL_INSTANCE", "Generic Windows component",
            "Microsoft", "System", "Microsoft", "1.0", null,
            "oem42.inf", true, true, 0, [id], []);

    private static DriverUpdateInfo Offer(IReadOnlyList<string> ids) =>
        new("00000000-0000-4000-8000-000000000463",
            "Windows driver entry", null, "System", "Microsoft",
            null, null, false, "Microsoft", null,
            @"PCI\VEN_9999&DEV_0000", null,
            SearchSource: "Windows Update online",
            AdditionalHardwareIds: ids,
            AdditionalHardwareIdsIncomplete: ids.Count >=
                WuaDriverEntryLimits.MaximumAdditionalIds);

    [Fact]
    public void MoreThanSixtyFourRareVendorsCannotHideKnownAudioAndSwcBuses()
    {
        var input = Common(1170).Concat(RareManufacturers(100))
            .Concat(KnownCrossBusModels).ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(WuaDriverEntryLimits.MaximumAdditionalIds,
            selected.HardwareIds.Count);
        Assert.Equal(input.Take(960), selected.HardwareIds.Take(960));
        Assert.Equal(input.Length - 1024, selected.DeferredIds);
        Assert.True(selected.DeferredIds > 0);
        Assert.Equal(1024, selected.HardwareIds.Distinct(
            StringComparer.OrdinalIgnoreCase).Count());

        var offer = Offer(selected.HardwareIds);
        foreach (var id in KnownCrossBusModels)
        {
            Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(id));
            Assert.NotNull(PnpKnownVendorIdentity.FromId(id));
            Assert.Contains(id, selected.HardwareIds);
            Assert.Equal(2, DriverUpdateMatcher.MatchStrength(Device(id), offer));
            var matched = DeviceWuaMatchGroups.Create(Device(id), [offer]);
            Assert.Equal(offer.Id, Assert.Single(matched.Ordinary).Id);
            Assert.Empty(matched.ReviewOnly);
        }
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([offer]));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"SWC\VEN_17CB&DEV_1002"), offer));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"INTELAUDIO\FUNC_01&VEN_10DE&DEV_009C"), offer));
    }

    [Fact]
    public void MergedWuaPassesStillProtectTheseLateKnownVendorModels()
    {
        var input = Common(2048).Concat(RareManufacturers(100))
            .Concat(KnownCrossBusModels).ToArray();
        var selected = WuaDriverEntryIdSelection.SelectMerged(input);

        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.Equal(input.Length - 1024, selected.DeferredIds);
        Assert.Equal(input.Take(960), selected.HardwareIds.Take(960));
        var offer = Offer(selected.HardwareIds);
        foreach (var model in KnownCrossBusModels)
        {
            Assert.Contains(model, selected.HardwareIds);
            Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
                Device(model), offer));
        }
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([offer]));
    }

    [Fact]
    public void SpecialAudioAndStorageBucketsStayIndependentOfMajorVendor()
    {
        var input = Common(1170).Concat(RareManufacturers(100))
            .Concat([Sndw, Acp, Sdca, Storage, NvidiaIntelAudio])
            .ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);

        foreach (var exact in new[] { Sndw, Acp, Sdca, Storage,
            NvidiaIntelAudio })
        {
            Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(exact));
            Assert.Contains(exact, selected.HardwareIds);
            Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
                Device(exact), Offer(selected.HardwareIds)));
        }
    }

    [Fact]
    public void InvalidPseudoVendorAndHidPeripheralCannotStealMajorSlots()
    {
        var invalid = new[]
        {
            @"INTELAUDIO\FUNC_01&VEN_10DE",
            @"INTELAUDIO\FUNC_01&VEN_10DE&FAKE_009B",
            @"INTELAUDIO\FUNC_01&VEN_1002&DEV_AA01\PRIVATE_INSTANCE",
            @"SWC\VEN_17CB",
            @"SWC\VEN_14E4&DEV_0123&FAKE_01",
            @"SWC\VEN_14C3&DEV_3210" + "\t",
            @"HDAUDIO\FUNC_02&VEN_10EC",
            @"PCI\VEN_10DE&DEV_28A0&FAKE_01"
        };
        Assert.Equal(PnpKnownVendorIdentity.Vendor.Logitech,
            PnpKnownVendorIdentity.FromId(Logitech));

        var input = Common(1170).Concat(RareManufacturers(100))
            .Concat(invalid).Concat([Logitech])
            .Concat(KnownCrossBusModels).ToArray();
        var selected = WuaDriverEntryIdSelection.Select(input);

        Assert.Equal(1024, selected.HardwareIds.Count);
        Assert.All(invalid, id =>
        {
            Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(id));
            Assert.DoesNotContain(id, selected.HardwareIds);
        });
        // Logitech VID+PID remains eligible for the rare HID-product
        // fair bucket, not impersonation as NVIDIA/Intel vendor.
        Assert.DoesNotContain(Logitech, selected.HardwareIds);
        Assert.All(KnownCrossBusModels,
            id => Assert.Contains(id, selected.HardwareIds));
        Assert.DoesNotContain(selected.HardwareIds,
            id => id.Contains("PRIVATE_", StringComparison.Ordinal));
    }

    [Fact]
    public void HistoricalSmallPassAndCapacityStillHaveSameSemantics()
    {
        var small = Common(12).Concat(KnownCrossBusModels).ToArray();
        var selection = WuaDriverEntryIdSelection.Select(small);
        Assert.Equal(small, selection.HardwareIds);
        Assert.Equal(0, selection.DeferredIds);
        Assert.Throws<InvalidDataException>(() =>
            WuaDriverEntryIdSelection.Select(Common(
                WuaDriverEntryLimits.MaximumEntriesToInspect + 1)));
        Assert.Throws<InvalidDataException>(() =>
            WuaDriverEntryIdSelection.SelectMerged(Common(
                2 * WuaDriverEntryLimits.MaximumEntriesToInspect + 2)));
    }
}
