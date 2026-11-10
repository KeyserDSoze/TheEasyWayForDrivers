using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaHidUsbStorVariantReviewTests
{
    private const string HidBase = @"HID\VID_046D&PID_C534";
    private const string HidComplete = @"HID\VID_046D&PID_C534&MI_00&COL01";
    private const string UsbStorBase =
        @"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_Blade";
    private const string UsbStorComplete =
        @"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_Blade&Rev_1.00";

    private static DriverInfo Device(string id,
        IReadOnlyList<string>? compatible = null) =>
        new(@"HID\PRIVATE_DEVICE_INSTANCE", "Generic Windows device",
            "Microsoft", "HIDClass", "Microsoft", "1.0", null,
            "oem1.inf", true, true, 0, [id], compatible ?? []);

    private static DriverUpdateInfo Wua(
        string id, string source = "Windows Update online",
        string title = "Windows device driver") =>
        new("00000000-0000-4000-8000-000000000341", title,
            null, "HIDClass", "Microsoft", null, null, false,
            "Microsoft", null, id, null, SearchSource: source);

    private static DriverSearchReport Report(DriverUpdateInfo update) =>
        new([update], [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "Verified WUA pass")
        ]);

    [Theory]
    [InlineData(HidComplete)]
    [InlineData(@"HID\VID_046D&PID_C534&REV_0100")]
    [InlineData(@"HID\VID_046D&PID_C534&IG_00")]
    [InlineData(@"HID\VID_046D&PID_C534&MI_01&COL02")]
    public void StrictHidSuffixToSameVidAndPidIsOnlyAReviewHint(
        string complete)
    {
        var device = Device(complete);
        var update = Wua(HidBase);

        Assert.True(HidPnpMatchPolicy.IsSpecificHardwareId(complete));
        Assert.True(QuickUpdatePolicy.IsEligible(update));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, update));
        Assert.Contains(HidBase,
            WuaGenericModelCorrelation.ReviewModelKeys(device));
        Assert.True(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(
            device, update));

        var prepared = DeviceWuaMatchGroups.Prepare([update]);
        Assert.True(prepared.UsesPnpIndex);
        var result = prepared.ForDevice(device);
        Assert.Empty(result.Ordinary);
        Assert.Empty(result.ReviewOnly);
        Assert.Equal(update.Id, Assert.Single(result.ModelOnlyReview!).Id);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(result.Ordinary));
        Assert.Empty(DeviceWuaMatchGroups.Create(device, []).ModelOnlyReview!);
    }

    [Fact]
    public void StrictUsbStorRevisionOnlyYieldsReviewForSameVendorAndProduct()
    {
        var device = Device(UsbStorComplete);
        var update = Wua(UsbStorBase);
        Assert.True(UsbStorPnpMatchPolicy.IsSpecificHardwareId(UsbStorComplete));
        Assert.True(UsbStorPnpMatchPolicy.IsSpecificHardwareId(UsbStorBase));
        Assert.Contains(UsbStorBase,
            WuaGenericModelCorrelation.ReviewModelKeys(device));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, update));
        Assert.True(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(
            device, update));

        var result = DeviceWuaMatchGroups.Create(device, [update]);
        Assert.Empty(result.Ordinary);
        Assert.Empty(result.ReviewOnly);
        Assert.Equal(update.Id, Assert.Single(result.ModelOnlyReview!).Id);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(result.Ordinary));
    }

    [Theory]
    [InlineData(HidComplete, @"HID\VID_046D&PID_C52B")]
    [InlineData(HidComplete, @"HID\VID_045E&PID_C534")]
    [InlineData(HidComplete, @"HID\VID_046D")]
    [InlineData(HidComplete, @"HID\HID_DEVICE")]
    [InlineData(HidComplete, @"HID\VID_046D&PID_C534&MI_02")]
    [InlineData(UsbStorComplete,
        @"USBSTOR\Disk&Ven_SanDisk&Prod_Other")]
    [InlineData(UsbStorComplete,
        @"USBSTOR\Disk&Ven_Samsung&Prod_Cruzer_Blade")]
    [InlineData(UsbStorComplete, @"USBSTOR\GenDisk")]
    [InlineData(UsbStorComplete, @"USBSTOR\Disk&Ven_SanDisk")]
    public void OtherVendorProductOrGenericWuaNeverCreatesReview(
        string deviceId, string wuaId)
    {
        var device = Device(deviceId);
        var update = Wua(wuaId);

        Assert.False(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(
            device, update));
        Assert.Empty(DeviceWuaMatchGroups.Create(
            device, [update]).ModelOnlyReview!);
    }

    [Theory]
    [InlineData(@"HID\VID_046D&PID_C534&FAKE_01", HidBase)]
    [InlineData(@"HID\VID_046D&PID_C534\PRIVATE_SERIAL", HidBase)]
    [InlineData(@"HID\VID_046D", HidBase)]
    [InlineData(@"HID\HID_DEVICE", HidBase)]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_Blade&FAKE_01",
        UsbStorBase)]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk", UsbStorBase)]
    [InlineData(@"USBSTOR\GenDisk", UsbStorBase)]
    [InlineData(@"USBSTOR\DiskSEAGATE_ST39102LW_______0004",
        @"USBSTOR\DiskSEAGATE_ST39102LW_______")]
    public void GenericMalformedInstanceAndPaddedIdsNeverInventReview(
        string deviceId, string wuaId)
    {
        var device = Device(deviceId);
        Assert.Empty(WuaGenericModelCorrelation.ReviewModelKeys(device));
        Assert.False(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(
            device, Wua(wuaId)));
    }

    [Fact]
    public void SourceGuidFirmwareOrTitleHeuristicsNeverProvideVariantEvidence()
    {
        var device = Device(HidComplete);
        var wrongGuid = Wua(HidBase) with { Id = "unverified" };
        var untrusted = Wua(HidBase, source: "Untrusted OEM catalog");
        var firmware = Wua(HidBase, title: "Firmware Update");
        var titleOnly = Wua(@"PCI\VEN_9999&DEV_0001",
            title: "Logitech HID VID_046D PID_C534");
        foreach (var update in new[] {
            wrongGuid, untrusted, firmware, titleOnly
        })
        {
            Assert.False(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(
                device, update));
            Assert.Empty(DeviceWuaMatchGroups.Create(
                device, [update]).ModelOnlyReview!);
        }
    }

    [Fact]
    public void ExistingWindowsCompatibleIdIsARealExactMatchNotAWeakHint()
    {
        foreach (var (deviceId, baseModel) in new[] {
            (HidComplete, HidBase), (UsbStorComplete, UsbStorBase)
        })
        {
            var device = Device(deviceId, [baseModel]);
            var update = Wua(baseModel);
            var groups = DeviceWuaMatchGroups.Create(device, [update]);
            Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
                device, update));
            Assert.Equal(update.Id, Assert.Single(groups.Ordinary).Id);
            Assert.Empty(groups.ModelOnlyReview!);
            Assert.False(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(
                device, update));
        }
    }

    [Fact]
    public void IndexedAndAggregateDiagnosticsAgreeWithoutLeakingAnyIds()
    {
        var hid = Device(HidComplete);
        var storage = Device(UsbStorComplete);
        var hidOffer = Wua(HidBase);
        var storageOffer = Wua(UsbStorBase) with
        {
            Id = "00000000-0000-4000-8000-000000000342"
        };
        var missing = Wua(@"PCI\VEN_8086&DEV_51F1") with
        {
            Id = "00000000-0000-4000-8000-000000000343"
        };
        var report = new DriverSearchReport(
            [hidOffer, storageOffer, missing],
            [new DriverSearchPassStatus("Windows Update online",
                "Completed", 3, "Verified WUA pass")]);

        var audit = WuaPnpAssociationAudit.Create(
            [hid, storage], report, inventoryComplete: true);
        Assert.Equal(3, audit.ConfirmedGuids);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(0, audit.CompatibleOnly);
        Assert.Equal(2, audit.GenericModelOnly);
        Assert.Equal(1, audit.Unattributed);
        Assert.False(audit.IsInstallable);

        var exported = ScanCoverageText.Create(report, [], 2, 0, 0,
            [hid, storage], inventoryComplete: true);
        Assert.Contains("solo modello generico", exported);
        Assert.DoesNotContain(HidComplete, exported);
        Assert.DoesNotContain(HidBase, exported);
        Assert.DoesNotContain(UsbStorComplete, exported);
        Assert.DoesNotContain(UsbStorBase, exported);
        Assert.DoesNotContain("PRIVATE", exported);
    }
}
