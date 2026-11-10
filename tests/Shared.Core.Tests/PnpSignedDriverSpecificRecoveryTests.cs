using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class PnpSignedDriverSpecificRecoveryTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Logitech = @"HID\VID_046D&PID_C534&MI_00&COL01";
    private const string Sandisk =
        @"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_Blade&Rev_1.00";

    private static DriverInfo Device(PnpSetupApiIdentity.Enrichment ids) =>
        new(@"PCI\PRIVATE_DEVICE_INSTANCE", "Generic Windows device",
            "Microsoft", "Unknown", "Microsoft", "1.0", null,
            "oem1.inf", true, true, 0, ids.HardwareIds,
            ids.CompatibleIds,
            PnpIdsRecoveredFromSignedDriver: ids.UsedSignedDriver,
            PnpIdsRecoveredFromSetupApi: ids.UsedSetupApi);

    private static DriverUpdateInfo Wua(string id) =>
        new("00000000-0000-4000-8000-000000000340",
            "PnP driver update", null, "Unknown", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    [Theory]
    [InlineData(@"PCI\VEN_10DE", Nvidia)]
    [InlineData(@"PCI\CC_0280", Killer)]
    [InlineData(@"USB\VID_8087", @"USB\VID_8087&PID_0033")]
    [InlineData(@"USB\CLASS_03", @"USB\VID_046D&PID_C534")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC",
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295")]
    [InlineData(@"INTELAUDIO\FUNC_01",
        @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295")]
    [InlineData(@"SWC\VEN_8086", @"SWC\VEN_8086&DEV_51F1")]
    [InlineData(@"HID\VID_046D", Logitech)]
    [InlineData(@"HID\HID_DEVICE_SYSTEM_MOUSE", Logitech)]
    [InlineData(@"USBSTOR\GenDisk", Sandisk)]
    public void ExactDeviceSignedModelRecoversOnlyWhenNativeUnavailable(
        string generic, string specific)
    {
        Assert.True(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [generic]));
        var result = PnpSetupApiIdentity.Merge(
            [generic], [], [], [], specific, null);

        Assert.Equal([specific, generic], result.HardwareIds);
        Assert.True(result.UsedSignedDriver);
        Assert.False(result.UsedSetupApi);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(result), Wua(specific)));
        Assert.Empty(DeviceWuaMatchGroups.Create(Device(result), []).Ordinary);
        Assert.Single(DeviceWuaMatchGroups.Create(
            Device(result), [Wua(specific)]).Ordinary);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE", Killer)]
    [InlineData(@"PCI\CC_0280", @"USB\VID_8087&PID_0033")]
    [InlineData(@"USB\VID_8087", @"USB\VID_046D&PID_C534")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC",
        @"HDAUDIO\FUNC_02&VEN_10EC&DEV_0295")]
    [InlineData(@"SWC\VEN_8086", @"SWC\VEN_10DE&DEV_28A0")]
    [InlineData(@"HID\VID_046D", @"HID\VID_045E&PID_028E")]
    [InlineData(@"USBSTOR\GenDisk",
        @"USBSTOR\CdRomSAMSUNG_PORTABLE_DVD____0001")]
    [InlineData(@"USBSTOR\Disk",
        @"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer&FAKE_01")]
    [InlineData(@"PCI\VEN_10DE", @"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL")]
    public void WrongBusVendorFunctionTypeOrMalformedSignedIdFailsClosed(
        string generic, string foreign)
    {
        var result = PnpSetupApiIdentity.Merge(
            [generic], [], [], [], foreign, null);

        Assert.Equal([generic], result.HardwareIds);
        Assert.False(result.UsedSignedDriver);
        Assert.False(result.UsedSetupApi);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(result), Wua(foreign)));
    }

    [Fact]
    public void CompleteWmiAndValidatedNativeModelsKeepPrecedence()
    {
        var complete = PnpSetupApiIdentity.Merge(
            [Nvidia], [], [Killer], [], Killer, null);
        Assert.Equal([Nvidia], complete.HardwareIds);
        Assert.False(complete.UsedSignedDriver);
        Assert.False(complete.UsedSetupApi);

        var native = PnpSetupApiIdentity.Merge(
            [@"PCI\VEN_10DE"], [], [Nvidia], [],
            @"PCI\VEN_10DE&DEV_1F15", null);
        Assert.Equal([Nvidia, @"PCI\VEN_10DE"], native.HardwareIds);
        Assert.True(native.UsedSetupApi);
        Assert.False(native.UsedSignedDriver);
        Assert.DoesNotContain(@"PCI\VEN_10DE&DEV_1F15",
            native.HardwareIds);
    }

    [Fact]
    public void ConflictingOrMixedGenericWmiCannotBorrowSignedHardware()
    {
        IReadOnlyList<string>[] conflicts =
        [
            [@"PCI\VEN_10DE", @"PCI\VEN_8086"],
            [@"HID\VID_046D", @"HID\VID_045E"],
            [@"USBSTOR\GenDisk", @"USBSTOR\GenCdRom"],
            [@"USB\VID_8087", @"HDAUDIO\FUNC_01"]
        ];
        foreach (var ids in conflicts)
        {
            Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
                ids));
            var merged = PnpSetupApiIdentity.Merge(
                ids, [], [], [], Nvidia, null);
            Assert.Equal(ids, merged.HardwareIds);
            Assert.False(merged.UsedSignedDriver);
        }
    }

    [Fact]
    public void SignedCompatibleFallbackAndMissingHardwareStillUseOldRules()
    {
        var compatible = PnpSetupApiIdentity.Merge(
            [@"PCI\VEN_10DE"], [], [], [], Killer, Nvidia);
        Assert.Equal([@"PCI\VEN_10DE"], compatible.HardwareIds);
        Assert.Equal([Nvidia], compatible.CompatibleIds);
        Assert.True(compatible.UsedSignedDriver);
        Assert.False(compatible.UsedSetupApi);

        var missing = PnpSetupApiIdentity.Merge(
            [], [], [], [], Nvidia, null);
        Assert.Equal([Nvidia], missing.HardwareIds);
        Assert.True(missing.UsedSignedDriver);
    }

    [Fact]
    public void AnonymousReportCountsSignedFallbackWithoutLeakingDeviceIds()
    {
        var merged = PnpSetupApiIdentity.Merge(
            [@"HID\VID_046D"], [], [], [], Logitech, null);
        var report = ScanCoverageText.Create(
            null, [], 1, 0, 0, [Device(merged)], inventoryComplete: true);
        Assert.True(merged.UsedSignedDriver);
        Assert.False(merged.UsedSetupApi);
        Assert.DoesNotContain("PRIVATE_DEVICE_INSTANCE", report);
        Assert.DoesNotContain(Logitech, report);
        Assert.DoesNotContain(@"HID\VID_046D", report);
        Assert.DoesNotContain("Generic Windows device", report);
    }
}
