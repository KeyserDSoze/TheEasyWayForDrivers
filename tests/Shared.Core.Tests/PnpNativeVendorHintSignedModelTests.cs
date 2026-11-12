using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// WMI may omit the ID while same-instance SetupAPI lists only a generic
/// vendor/class token. Preserve a validated SignedDriver model of the SAME
/// vendor without borrowing identities or claiming driver availability.
/// </summary>
public sealed class PnpNativeVendorHintSignedModelTests
{
    private static DriverInfo Device(
        PnpSetupApiIdentity.Enrichment ids, string deviceClass = "Unknown") =>
        new("PRIVATE_SAME_INSTANCE_SERIAL", "Windows generic device",
            "Microsoft", deviceClass, "Microsoft", "1.0", null,
            "PRIVATE_oem42.inf", true, true, 0,
            ids.HardwareIds, ids.CompatibleIds,
            PnpIdsRecoveredFromSignedDriver: ids.UsedSignedDriver,
            PnpIdsRecoveredFromSetupApi: ids.UsedSetupApi);

    private static DriverUpdateInfo Wua(string id) =>
        new("00000000-0000-4000-8000-000000000381",
            "Windows driver candidate", null, "Unknown", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    [Theory]
    [InlineData(@"PCI\VEN_10DE&CC_0300",
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA")]
    [InlineData(@"PCI\VEN_8086&CC_0280",
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE",
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    [InlineData(@"USB\VID_0955", @"USB\VID_0955&PID_7019")]
    [InlineData(@"HID\VID_046D", @"HID\VID_046D&PID_C534")]
    [InlineData(@"SWC\VEN_10DE", @"SWC\VEN_10DE&DEV_28A0")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D",
        @"SNDW\CLASS_00&MAN_025D&PART_0711")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D",
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711")]
    [InlineData(@"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022",
        @"ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2")]
    public void MissingWmiAndOnlyVendorBoundNativeHintRecoverSameInstanceSignedModel(
        string generic, string specific)
    {
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(generic));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(specific));

        var ids = PnpSetupApiIdentity.Merge(
            ["PRIVATE_WMI_INSTANCE_PATH"], [],
            [generic], [], specific, null);

        Assert.Equal([specific, generic], ids.HardwareIds);
        Assert.True(ids.UsedSetupApi);
        Assert.True(ids.UsedSignedDriver);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(Device(ids), Wua(specific)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(Device(ids), Wua(generic)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(ids), Wua(@"PCI\VEN_9999&DEV_0001")));
    }

    [Fact]
    public void NvidiaGraphicsAndKillerNetworkBecomeDetectableWithoutMarketingNames()
    {
        var nvidia = PnpSetupApiIdentity.Merge([], [],
            [@"PCI\VEN_10DE&CC_0300"], [],
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA", null);
        var gpu = Device(nvidia, "Display");
        Assert.True(NvidiaHardwareDetector.IsNvidiaDevice(gpu));
        Assert.Equal("NVIDIA",
            VendorSourceDiscovery.GetRecognizedSupportVendor(gpu));
        Assert.Equal(1, NvidiaHardwareDetector.CountNvidiaDevices([gpu]));

        var killer = PnpSetupApiIdentity.Merge([], [],
            [@"PCI\VEN_8086&CC_0280"], [],
            @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56", null);
        var wifi = Device(killer, "Net");
        Assert.True(KillerWifiHardwareDetector.IsKnownAx1675i(wifi));
        Assert.True(IntelHardwareDetector.IsIntelDevice(wifi));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            wifi, Wua(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56")));
        Assert.Empty(DeviceWuaMatchGroups.Create(wifi, []).Ordinary);
    }

    [Fact]
    public void RecoveredNativeVendorFiltersContradictoryCompatibleIds()
    {
        const string primary = @"PCI\VEN_10DE&DEV_28A0";
        const string foreign = @"PCI\VEN_8086&DEV_51F1";
        var ids = PnpSetupApiIdentity.Merge([], [],
            [@"PCI\VEN_10DE&CC_0300"],
            [foreign, @"PCI\CC_0300"], primary, foreign);
        Assert.Equal([primary, @"PCI\VEN_10DE&CC_0300"], ids.HardwareIds);
        Assert.Equal([@"PCI\CC_0300"], ids.CompatibleIds);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(ids), Wua(foreign)));
        Assert.True(ids.UsedSignedDriver);
        Assert.True(ids.UsedSetupApi);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&CC_0300",
        @"PCI\VEN_8086&DEV_51F1")]
    [InlineData(@"PCI\CC_0300",
        @"PCI\VEN_10DE&DEV_28A0")]
    [InlineData(@"HDAUDIO\FUNC_01",
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    [InlineData(@"USB\Class_E0&SubClass_01&Prot_01",
        @"USB\VID_0955&PID_7019")]
    [InlineData(@"USB\VID_0955", @"USB\VID_0CF3&PID_E300")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE",
        @"HDAUDIO\FUNC_02&VEN_10DE&DEV_009B")]
    [InlineData(@"SWC\VEN_10DE", @"PCI\VEN_10DE&DEV_28A0")]
    [InlineData(@"HID\VID_046D",
        @"HID\VID_1532&PID_0100")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D",
        @"SNDW\CLASS_00&MAN_0242&PART_0711")]
    [InlineData(@"PCI\VEN_10DE&CC_0300",
        @"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE")]
    public void RejectsForeignClassOnlyAndInvalidSignedModels(
        string nativeHint, string signedModel)
    {
        var ids = PnpSetupApiIdentity.Merge([], [],
            [nativeHint], [], signedModel, null);
        Assert.Equal([nativeHint], ids.HardwareIds);
        Assert.False(ids.UsedSignedDriver);
        Assert.True(ids.UsedSetupApi);
    }

    [Fact]
    public void ConflictingNativeVendorHintsDoNotSelectArbitrarySignedGpu()
    {
        var hints = new[] { @"PCI\VEN_10DE", @"PCI\VEN_8086" };
        var ids = PnpSetupApiIdentity.Merge([], [], hints, [],
            @"PCI\VEN_10DE&DEV_28A0", null);
        Assert.Equal(hints, ids.HardwareIds);
        Assert.False(ids.UsedSignedDriver);

        var mixed = new[] { @"PCI\VEN_10DE", @"USB\VID_0955" };
        var mixedIds = PnpSetupApiIdentity.Merge([], [], mixed, [],
            @"PCI\VEN_10DE&DEV_28A0", null);
        Assert.Equal(mixed, mixedIds.HardwareIds);
        Assert.False(mixedIds.UsedSignedDriver);
    }

    [Fact]
    public void SpecificNativeAndAuthoritativeWmiHardwareNeverOverridden()
    {
        const string complete = @"PCI\VEN_10DE&DEV_28A0";
        const string other = @"PCI\VEN_10DE&DEV_1F15";
        var native = PnpSetupApiIdentity.Merge([], [],
            [complete], [], other, null);
        Assert.Equal([complete], native.HardwareIds);
        Assert.False(native.UsedSignedDriver);
        Assert.True(native.UsedSetupApi);

        var wmi = PnpSetupApiIdentity.Merge([complete], [],
            [@"PCI\VEN_10DE"], [], other, null);
        Assert.Equal([complete], wmi.HardwareIds);
        Assert.False(wmi.UsedSignedDriver);
        Assert.False(wmi.UsedSetupApi);
    }

    [Fact]
    public void RecoveryNeverLeaksPrivateDeviceInstanceOrCertifiesInstallability()
    {
        var ids = PnpSetupApiIdentity.Merge([], [],
            [@"PCI\VEN_10DE&CC_0300"], [],
            @"PCI\VEN_10DE&DEV_28A0", null);
        var report = ScanCoverageText.Create(null, [], 1, 0, 0,
            [Device(ids, "Display")], inventoryComplete: true);
        Assert.DoesNotContain("PRIVATE", report);
        Assert.DoesNotContain(@"PCI\VEN_10DE", report);
        Assert.DoesNotContain("00000000-0000", report);

        var search = new DriverSearchReport(
            [Wua(@"PCI\VEN_10DE&DEV_28A0")],
            [new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "pass completo")]);
        var assessment = DeviceWuaMatchGroups.Create(
            Device(ids, "Display"), search.Updates);
        Assert.Single(assessment.Ordinary);
        // Only confirmed WUA GUIDs, not synthetic SignedDriver metadata.
        Assert.Empty(DeviceWuaMatchGroups.Create(Device(ids, "Display"), [])
            .Ordinary);
    }
}
