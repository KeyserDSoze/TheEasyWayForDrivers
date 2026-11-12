using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A partially identified non-PCI device can recover matching native
/// models but must not inherit another device's CompatibleID from the
/// SetupAPI or SignedDriver snapshot. Correlation is read-only.
/// </summary>
public sealed class PnpNonPciCompatibleScopeTests
{
    private const string WuaGuid =
        "00000000-0000-4000-8000-000000000379";

    private static DriverInfo Device(PnpSetupApiIdentity.Enrichment ids) =>
        new("PRIVATE_INSTANCE_SERIAL", "Generic Windows device",
            "Microsoft", "Unknown", "Microsoft", "1.0", null,
            "PRIVATE_OEM.inf", true, true, 0,
            ids.HardwareIds, ids.CompatibleIds,
            PnpIdsRecoveredFromSetupApi: ids.UsedSetupApi,
            PnpIdsRecoveredFromSignedDriver: ids.UsedSignedDriver);

    private static DriverUpdateInfo Offer(string hardwareId) =>
        new(WuaGuid, "Windows driver", null, "Unknown", "Microsoft",
            null, null, false, "Microsoft", null, hardwareId, null,
            SearchSource: "Windows Update online");

    [Theory]
    [InlineData(@"USB\VID_0955", @"USB\VID_0955&PID_7019",
        @"USB\VID_0CF3&PID_E300")]
    [InlineData(@"HID\VID_046D", @"HID\VID_046D&PID_C534",
        @"HID\VID_1532&PID_0100")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE",
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B",
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295")]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC",
        @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295",
        @"INTELAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    [InlineData(@"SWC\VEN_10DE", @"SWC\VEN_10DE&DEV_28A0",
        @"SWC\VEN_8086&DEV_51F1")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D",
        @"SNDW\CLASS_00&MAN_025D&PART_0711",
        @"SNDW\CLASS_00&MAN_0242&PART_0711")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D",
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711",
        @"SOUNDWIRE\SDCA_06&MAN_0242&FUNC_0711")]
    [InlineData(@"USBSTOR\GenDisk",
        @"USBSTOR\DiskSEAGATE_ST39102LW_______0004",
        @"USBSTOR\CdRomSAMSUNG_PORTABLE_DVD____0001")]
    public void ForeignNativeAndSignedCompatibleAreRejectedEvenWithValidHardware(
        string wmi, string recovered, string foreign)
    {
        Assert.True(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery([wmi]));
        var merged = PnpSetupApiIdentity.Merge([wmi], [],
            [recovered], [foreign], null, foreign);

        Assert.Equal([recovered, wmi], merged.HardwareIds);
        Assert.Empty(merged.CompatibleIds);
        Assert.True(merged.UsedSetupApi);
        Assert.False(merged.UsedSignedDriver);
        var device = Device(merged);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            device, Offer(recovered)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            device, Offer(foreign)));
        Assert.DoesNotContain(foreign, merged.HardwareIds);
        Assert.DoesNotContain(foreign, merged.CompatibleIds);
    }

    [Theory]
    [InlineData(@"USB\VID_0955", @"USB\VID_0CF3&PID_E300")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE",
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295")]
    [InlineData(@"SWC\VEN_10DE", @"SWC\VEN_8086&DEV_51F1")]
    [InlineData(@"HID\VID_046D", @"PCI\VEN_10DE&DEV_28A0")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D",
        @"SOUNDWIRE\SDCA_07&MAN_025D&FUNC_0711")]
    public void ForeignSignedCompatibleCannotBypassMissingNativeMetadata(
        string wmi, string foreign)
    {
        var merged = PnpSetupApiIdentity.Merge(
            [wmi], [], [], [], null, foreign);
        Assert.Equal([wmi], merged.HardwareIds);
        Assert.Empty(merged.CompatibleIds);
        Assert.False(merged.UsedSetupApi);
        Assert.False(merged.UsedSignedDriver);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(merged), Offer(foreign)));
    }

    [Theory]
    [InlineData(@"USB\VID_0955", @"USB\VID_0955&PID_7019")]
    [InlineData(@"HID\VID_046D", @"HID\VID_046D&PID_C534")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE",
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    [InlineData(@"SWC\VEN_10DE", @"SWC\VEN_10DE&DEV_28A0")]
    [InlineData(@"SOUNDWIRE\SDCA_06&MAN_025D",
        @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711")]
    public void SameScopeNativeAndSignedCompatibleRemainAvailable(
        string wmi, string specific)
    {
        var native = PnpSetupApiIdentity.Merge(
            [wmi], [], [], [specific], null, null);
        Assert.Equal([specific], native.CompatibleIds);
        Assert.True(native.UsedSetupApi);
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            Device(native), Offer(specific)));

        var signed = PnpSetupApiIdentity.Merge(
            [wmi], [], [], [], null, specific);
        Assert.Equal([specific], signed.CompatibleIds);
        Assert.True(signed.UsedSignedDriver);
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            Device(signed), Offer(specific)));
    }

    [Theory]
    [InlineData(@"HID\VID_046D", @"HID\HID_DEVICE")]
    [InlineData(@"USB\VID_0955", @"USB\Class_E0&SubClass_01&Prot_01")]
    [InlineData(@"USBSTOR\GenDisk", @"USBSTOR\Disk")]
    public void SameBusGenericCompatibleIsPreserved(
        string wmi, string generic)
    {
        var native = PnpSetupApiIdentity.Merge(
            [wmi], [], [], [generic], null, null);
        Assert.Equal([generic], native.CompatibleIds);
        Assert.True(native.UsedSetupApi);
        var signed = PnpSetupApiIdentity.Merge(
            [wmi], [], [], [], null, generic);
        Assert.Equal([generic], signed.CompatibleIds);
        Assert.True(signed.UsedSignedDriver);
        // Generic class codes never become specific install candidates.
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(native), Offer(generic)));
    }

    [Fact]
    public void ExistingWmiCompatibleStillOutranksConflictingNativeAndSigned()
    {
        var original = @"HID\HID_DEVICE";
        var merged = PnpSetupApiIdentity.Merge(
            [@"HID\VID_046D"], [original], [],
            [@"HID\VID_1532&PID_0100"], null,
            @"HID\VID_1532&PID_0100");
        Assert.Equal([original], merged.CompatibleIds);
        Assert.False(merged.UsedSignedDriver);
        Assert.False(merged.UsedSetupApi);
    }

    [Fact]
    public void ReportRemainsAnonymousAndNeverClaimsNewInstallableDriver()
    {
        var merged = PnpSetupApiIdentity.Merge(
            [@"HDAUDIO\FUNC_01&VEN_10DE"], [],
            [@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B"],
            [@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295"], null, null);
        var device = Device(merged);
        var report = ScanCoverageText.Create(null, [],
            1, 0, 0, [device], inventoryComplete: true);
        Assert.DoesNotContain("PRIVATE_", report);
        Assert.DoesNotContain(WuaGuid, report);
        Assert.DoesNotContain(@"HDAUDIO\", report);
        Assert.Contains("NON significa driver nuovi o installabili", report);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedForReport(null));
    }
}
