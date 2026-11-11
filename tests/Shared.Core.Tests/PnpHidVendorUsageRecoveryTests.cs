using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class PnpHidVendorUsageRecoveryTests
{
    private const string VendorUsage = @"HID\VID_046D&UP:0001_U:0006";
    private const string Logitech = @"HID\VID_046D&PID_C534&MI_00&COL01";
    private const string Microsoft = @"HID\VID_045E&PID_028E";
    private const string InvalidModel = @"HID\VID_046D&PID_C534&FAKE_00";

    private static DriverInfo Device(PnpSetupApiIdentity.Enrichment ids) =>
        new(@"HID\PRIVATE_DEVICE_INSTANCE", "Generic Windows keyboard",
            "Microsoft", "HIDClass", "Microsoft", "1.0", null,
            "oem1.inf", true, true, 0, ids.HardwareIds,
            ids.CompatibleIds,
            PnpIdsRecoveredFromSignedDriver: ids.UsedSignedDriver,
            PnpIdsRecoveredFromSetupApi: ids.UsedSetupApi);

    private static DriverUpdateInfo Offer(string hardwareId) =>
        new("00000000-0000-4000-8000-000000000342", "HID driver",
            null, "HIDClass", "Microsoft", null, null, false,
            "Microsoft", null, hardwareId, null,
            SearchSource: "Windows Update online");

    private static string Inf(string hardwareId) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\n%Maker%=Models,NTamd64\n" +
        "[Models.NTamd64]\n%Keyboard%=Install," + hardwareId;

    [Fact]
    public void OfficialVendorUsageShapeIsRetainedButNeverAnExactDeviceMatch()
    {
        Assert.True(PnpIdentifierFallback.IsStrictSingleId(VendorUsage));
        Assert.False(HidPnpMatchPolicy.IsSpecificHardwareId(VendorUsage));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(VendorUsage));

        var normalized = PnpSetupApiIdentity.SanitizeWmiIds([
            VendorUsage, VendorUsage.ToLowerInvariant(),
            @"HID_DEVICE_UP:0001_U:0006"
        ]);
        Assert.Equal([VendorUsage], normalized);
        Assert.True(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            normalized));

        var native = Encoding.Unicode.GetBytes(
            VendorUsage + "\0" + VendorUsage.ToLowerInvariant() + "\0\0");
        Assert.Equal([VendorUsage],
            PnpSetupApiIdentity.Decode(native,
                PnpSetupApiIdentity.RegMultiSz, (uint)native.Length));

        var unmodified = PnpSetupApiIdentity.Merge(
            [VendorUsage], [], [], [], null, null);
        var device = Device(unmodified);
        Assert.Equal([VendorUsage], unmodified.HardwareIds);
        Assert.False(unmodified.UsedSetupApi);
        Assert.False(unmodified.UsedSignedDriver);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            device, Offer(VendorUsage)));
        Assert.Empty(DeviceWuaMatchGroups.Create(
            device, [Offer(VendorUsage)]).Ordinary);
        Assert.Empty(InfModelEvidence.ReadMatchingIds(
            Inf(VendorUsage), [device]));
    }

    [Theory]
    [InlineData(VendorUsage)]
    [InlineData(@"hid\vid_046d&up:0001_u:0006")]
    public void GenericWmiUsageRecoversOnlySameVendorNativePid(
        string wmiUsage)
    {
        var ids = PnpSetupApiIdentity.Merge(
            [wmiUsage], [VendorUsage],
            [Microsoft, InvalidModel, @"HID\VID_046D", Logitech],
            [Microsoft], Microsoft, null);
        Assert.Equal([Logitech, wmiUsage], ids.HardwareIds);
        Assert.Equal([VendorUsage], ids.CompatibleIds);
        Assert.True(ids.UsedSetupApi);
        Assert.False(ids.UsedSignedDriver);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(ids), Offer(Logitech)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(ids), Offer(Microsoft)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(ids), Offer(wmiUsage)));
        Assert.Single(DeviceWuaMatchGroups.Create(
            Device(ids), [Offer(Logitech)]).Ordinary);

        var report = ScanCoverageText.Create(
            null, [], 1, 0, 0, [Device(ids)], inventoryComplete: true);
        Assert.Contains("SetupAPI Windows locale: 1 dispositivi", report);
        Assert.DoesNotContain(VendorUsage, report);
        Assert.DoesNotContain(Logitech, report);
        Assert.DoesNotContain("PRIVATE", report);
    }

    [Fact]
    public void SameVendorSignedDriverIsFallbackOnlyWhenNativeUnavailable()
    {
        var recovered = PnpSetupApiIdentity.Merge(
            [VendorUsage], [], [], [], Logitech, null);
        Assert.Equal([Logitech, VendorUsage], recovered.HardwareIds);
        Assert.True(recovered.UsedSignedDriver);
        Assert.False(recovered.UsedSetupApi);

        var rejected = PnpSetupApiIdentity.Merge(
            [VendorUsage], [], [], [], Microsoft, null);
        Assert.Equal([VendorUsage], rejected.HardwareIds);
        Assert.False(rejected.UsedSignedDriver);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(rejected), Offer(Microsoft)));

        var native = PnpSetupApiIdentity.Merge(
            [VendorUsage], [], [Logitech], [],
            @"HID\VID_046D&PID_C52B", null);
        Assert.Equal([Logitech, VendorUsage], native.HardwareIds);
        Assert.True(native.UsedSetupApi);
        Assert.False(native.UsedSignedDriver);
    }

    [Fact]
    public void VendorOnlyAndUsageWmiAgreeButConflictingVendorFailsClosed()
    {
        var agreed = PnpSetupApiIdentity.Merge(
            [@"HID\VID_046D", VendorUsage],
            [], [Microsoft, Logitech], [], null, null);
        Assert.Equal([Logitech, @"HID\VID_046D", VendorUsage],
            agreed.HardwareIds);
        Assert.True(agreed.UsedSetupApi);

        var conflict = new[] { VendorUsage,
            @"HID\VID_045E&UP:0001_U:0006" };
        Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            conflict));
        var blocked = PnpSetupApiIdentity.Merge(
            conflict, [], [Logitech, Microsoft], [], Logitech, null);
        Assert.Equal(conflict, blocked.HardwareIds);
        Assert.False(blocked.UsedSetupApi);
        Assert.False(blocked.UsedSignedDriver);

        var mixedBus = PnpSetupApiIdentity.Merge(
            [VendorUsage, @"USB\VID_046D"], [],
            [Logitech], [], Logitech, null);
        Assert.False(mixedBus.UsedSetupApi);
        Assert.False(mixedBus.UsedSignedDriver);
        Assert.Equal([VendorUsage, @"USB\VID_046D"],
            mixedBus.HardwareIds);
    }

    [Fact]
    public void CompleteWmiPidAlwaysOutranksNativeOrSignedRecovery()
    {
        var result = PnpSetupApiIdentity.Merge(
            [Logitech, VendorUsage], [], [Microsoft], [],
            Microsoft, null);
        Assert.Equal([Logitech, VendorUsage], result.HardwareIds);
        Assert.False(result.UsedSetupApi);
        Assert.False(result.UsedSignedDriver);
    }

    [Theory]
    [InlineData(@"HID\VID_046D&UP:0001_U:000")]
    [InlineData(@"HID\VID_046D&UP:0001_U:0006&FAKE_01")]
    [InlineData(@"HID\VID_046D&UP:0001_U:000G")]
    [InlineData(@"HID\VID_046D&UP:000Z_U:0006")]
    [InlineData(@"HID\VID_046&UP:0001_U:0006")]
    [InlineData(@"HID\VID_046D&UP:001_U:0006")]
    [InlineData(@"HID\VID_046D&UP:0001_U:0006\PRIVATE_SERIAL")]
    [InlineData(@"HID\VID_046D&PID_C534&UP:0001_U:0006")]
    [InlineData(@"USB\VID_046D&UP:0001_U:0006")]
    [InlineData(@"HID\VID_046D&UP:0001_U:0006 ")]
    [InlineData(@"HID_DEVICE_UP:0001_U:0006")]
    [InlineData(@"PCI\VEN_10DE:FAKE&DEV_28A0")]
    [InlineData(@"C:\PRIVATE_PATH")]
    public void MalformedOrUnqualifiedUsageCannotSupplyHardwareEvidence(
        string bad)
    {
        Assert.False(PnpIdentifierFallback.IsStrictSingleId(bad));
        Assert.Empty(PnpSetupApiIdentity.SanitizeWmiIds([bad]));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(PnpSetupApiIdentity.Merge([], [], [], [],
                null, null)), Offer(bad)));
    }
}
