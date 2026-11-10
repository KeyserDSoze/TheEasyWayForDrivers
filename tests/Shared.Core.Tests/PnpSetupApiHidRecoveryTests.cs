using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class PnpSetupApiHidRecoveryTests
{
    private const string Logitech = @"HID\VID_046D&PID_C534&MI_00&COL01";
    private const string LogitechOther = @"HID\VID_046D&PID_C52B&REV_0100";
    private const string Foreign = @"HID\VID_045E&PID_028E";
    private const string InvalidNative = @"HID\VID_046D&PID_C534&FAKE_01";

    private static DriverInfo Device(PnpSetupApiIdentity.Enrichment ids) =>
        new("HID\\PRIVATE_DEVICE_SERIAL", "Generic Windows mouse",
            "Microsoft", "HIDClass", "Microsoft",
            "1.0", null, "oem99.inf", true, true, 0,
            ids.HardwareIds, ids.CompatibleIds,
            PnpIdsRecoveredFromSetupApi: ids.UsedSetupApi);

    private static DriverUpdateInfo Wua(string id) =>
        new("00000000-0000-4000-8000-000000000338",
            "HID device driver", null, "HIDClass", "Microsoft",
            null, null, false, "Microsoft", null,
            id, null, SearchSource: "Windows Update online");

    private static string Inf(string id) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\n%Maker%=Models,NTamd64\n" +
        "[Models.NTamd64]\n%Mouse%=Install," + id;

    [Theory]
    [InlineData(@"HID\HID_DEVICE")]
    [InlineData(@"HID\HID_DEVICE_SYSTEM_MOUSE")]
    [InlineData(@"HID\HID_DEVICE_SYSTEM_KEYBOARD")]
    [InlineData(@"HID\HID_DEVICE_UP_0001")]
    public void ClassOnlyWmiRecoversOnlyCompleteSameInstanceHidModel(
        string generic)
    {
        Assert.True(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [generic]));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(generic));
        var ids = PnpSetupApiIdentity.Merge(
            [generic], [@"HID\HID_DEVICE"],
            [@"PCI\VEN_10DE&DEV_28A0", InvalidNative,
                @"HID\VID_046D", Logitech],
            [Foreign], Foreign, null);

        Assert.Equal(Logitech, ids.HardwareIds[0]);
        Assert.Contains(generic, ids.HardwareIds);
        Assert.DoesNotContain(Foreign, ids.HardwareIds);
        Assert.DoesNotContain(InvalidNative, ids.HardwareIds);
        Assert.DoesNotContain(@"HID\VID_046D", ids.HardwareIds);
        Assert.Equal([@"HID\HID_DEVICE"], ids.CompatibleIds);
        Assert.True(ids.UsedSetupApi);
        Assert.False(ids.UsedSignedDriver);

        var device = Device(ids);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            device, Wua(Logitech)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            device, Wua(generic)));
        Assert.Equal(2, InfModelEvidence.MatchStrength(
            device, InfModelEvidence.ReadDeclaredIds(Inf(Logitech))));
        Assert.Empty(InfModelEvidence.ReadMatchingIds(Inf(generic),
            [device]));

        var report = ScanCoverageText.Create(null, [], 1, 0, 0,
            [device], inventoryComplete: true);
        Assert.Contains("SetupAPI Windows locale: 1 dispositivi", report);
        Assert.DoesNotContain("PRIVATE", report);
        Assert.DoesNotContain(Logitech, report);
        Assert.DoesNotContain(generic, report);
    }

    [Theory]
    [InlineData(@"HID\VID_046D")]
    [InlineData(@"hid\vid_046d")]
    public void VendorOnlyHidWmiRecoversOnlyMatchingVid(string vendorOnly)
    {
        Assert.True(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [vendorOnly]));
        var ids = PnpSetupApiIdentity.Merge(
            [vendorOnly], [], [Foreign, Logitech, InvalidNative,
                LogitechOther], [], Foreign, null);
        Assert.Equal([Logitech, LogitechOther, vendorOnly], ids.HardwareIds);
        Assert.True(ids.UsedSetupApi);
        Assert.False(ids.UsedSignedDriver);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(ids), Wua(Logitech)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(ids), Wua(Foreign)));
    }

    [Fact]
    public void ConflictingVendorIdsNeverBorrowAnyNativeHid()
    {
        var original = new[]
        {
            @"HID\VID_046D", @"HID\VID_045E",
            @"HID\HID_DEVICE_SYSTEM_MOUSE"
        };
        Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            original));
        var ids = PnpSetupApiIdentity.Merge(
            original, [], [Logitech, Foreign], [], null, null);
        Assert.Equal(original, ids.HardwareIds);
        Assert.False(ids.UsedSetupApi);
    }

    [Theory]
    [InlineData(@"HID\VID_046D&PID_C534")]
    [InlineData(@"HID\MSFT0001")]
    [InlineData(@"HID\VID_046D&PID_")]
    [InlineData(@"HID\VID_046")]
    [InlineData(@"HID\HID_DEVICE_SYSTEM_GAME")]
    [InlineData(@"USB\VID_046D&PID_C534")]
    public void CompleteMalformedOrForeignWmiDoesNotTriggerRecovery(
        string original)
    {
        Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [original]));
        var ids = PnpSetupApiIdentity.Merge(
            [original], [], [Logitech], [], null, null);
        Assert.Equal([original], ids.HardwareIds);
        Assert.False(ids.UsedSetupApi);
    }

    [Fact]
    public void VendorAndGenericClassTogetherConstrainRecovery()
    {
        var ids = PnpSetupApiIdentity.Merge(
            [@"HID\VID_046D", @"HID\HID_DEVICE"],
            [], [Foreign, Logitech], [], null, null);
        Assert.Equal([Logitech, @"HID\VID_046D", @"HID\HID_DEVICE"],
            ids.HardwareIds);
        Assert.True(ids.UsedSetupApi);
        var pci = PnpSetupApiIdentity.Merge(
            [@"PCI\VEN_10DE"], [],
            [Logitech, @"PCI\VEN_10DE&DEV_28A0"], [], null, null);
        Assert.Equal(@"PCI\VEN_10DE&DEV_28A0", pci.HardwareIds[0]);
        Assert.DoesNotContain(Logitech, pci.HardwareIds);
    }

    [Fact]
    public void InvalidNativeHidKeepsGenericWmiAndNeverInventsMatch()
    {
        // A complete, same-instance SignedDriver HID model is now a valid
        // fallback; keep this negative case strictly malformed instead.
        var ids = PnpSetupApiIdentity.Merge(
            [@"HID\HID_DEVICE_SYSTEM_MOUSE"], [],
            [Foreign + @"\PRIVATE_SERIAL", @"HID\VID_046D",
                InvalidNative], [], InvalidNative, null);
        Assert.Equal([@"HID\HID_DEVICE_SYSTEM_MOUSE"], ids.HardwareIds);
        Assert.False(ids.UsedSetupApi);
        Assert.False(ids.UsedSignedDriver);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(ids), Wua(Logitech)));
    }

    [Fact]
    public void RecoveredHidDeviceMatchesOnlyExistingConfirmedWuaGuid()
    {
        var ids = PnpSetupApiIdentity.Merge(
            [@"HID\HID_DEVICE_SYSTEM_MOUSE"], [],
            [Logitech], [], null, null);
        var groups = DeviceWuaMatchGroups.Create(Device(ids), [Wua(Logitech)]);
        Assert.Single(groups.Ordinary);
        Assert.Empty(DeviceWuaMatchGroups.Create(Device(ids), []).Ordinary);
    }
}
