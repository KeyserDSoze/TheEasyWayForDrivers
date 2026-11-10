using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class PnpSetupApiUsbStorRecoveryTests
{
    private const string Seagate =
        @"USBSTOR\DiskSEAGATE_ST39102LW_______0004";
    private const string Samsung =
        @"USBSTOR\DiskSAMSUNG_SSD_980_________0001";
    private const string Sandisk =
        @"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_Blade&Rev_1.00";
    private const string CdRom =
        @"USBSTOR\CdRomSAMSUNG_PORTABLE_DVD____0001";
    private const string Invalid =
        @"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer&FAKE_01";

    private static DriverInfo Device(PnpSetupApiIdentity.Enrichment ids) =>
        new("USBSTOR\\PRIVATE_UNIT_SERIAL", "Generic USB drive",
            "Microsoft", "DiskDrive", "Microsoft", "1.0", null,
            "disk.inf", true, true, 0,
            ids.HardwareIds, ids.CompatibleIds,
            PnpIdsRecoveredFromSetupApi: ids.UsedSetupApi);

    private static DriverUpdateInfo Wua(string id) =>
        new("00000000-0000-4000-8000-000000000339",
            "Windows USB storage driver", null, "DiskDrive", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    private static string Inf(string id) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\n%Maker%=Models,NTamd64\n" +
        "[Models.NTamd64]\n%Storage%=Install," + id;

    [Theory]
    [InlineData(@"USBSTOR\Disk")]
    [InlineData(@"USBSTOR\GenDisk")]
    [InlineData(@"USBSTOR\RAW")]
    public void GenericStorageWmiRecoversValidatedExactSameInstanceModel(
        string generic)
    {
        Assert.True(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [generic]));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(generic));
        var ids = PnpSetupApiIdentity.Merge(
            [generic], [@"USBSTOR\GenDisk"],
            [@"PCI\VEN_10DE&DEV_28A0",
                @"USB\VID_0781&PID_5567",
                @"USBSTOR\DiskSEAGATE_", Invalid,
                Seagate, Sandisk],
            [Samsung], Samsung, null);
        Assert.Equal([Seagate, Sandisk, generic], ids.HardwareIds);
        Assert.Equal([@"USBSTOR\GenDisk"], ids.CompatibleIds);
        Assert.DoesNotContain(Samsung, ids.HardwareIds);
        Assert.DoesNotContain(Invalid, ids.HardwareIds);
        Assert.True(ids.UsedSetupApi);
        Assert.False(ids.UsedSignedDriver);

        var device = Device(ids);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            device, Wua(Seagate)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            device, Wua(generic)));
        Assert.Equal(2, InfModelEvidence.MatchStrength(
            device, InfModelEvidence.ReadDeclaredIds(Inf(Seagate))));
        Assert.Empty(InfModelEvidence.ReadMatchingIds(Inf(generic),
            [device]));

        var qa = ScanCoverageText.Create(null, [], 1, 0, 0,
            [device], inventoryComplete: true);
        Assert.Contains("SetupAPI Windows locale: 1 dispositivi", qa);
        Assert.DoesNotContain("PRIVATE", qa);
        Assert.DoesNotContain(Seagate, qa);
        Assert.DoesNotContain(generic, qa);
    }

    [Theory]
    [InlineData(@"USBSTOR\CdRom")]
    [InlineData(@"USBSTOR\GenCdRom")]
    public void CdRomClassCanRecoverOnlyMatchingTypedNativeModel(
        string generic)
    {
        Assert.True(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [generic]));
        var ids = PnpSetupApiIdentity.Merge(
            [generic], [],
            [Seagate, CdRom], [], Seagate, null);
        Assert.Equal([CdRom, generic], ids.HardwareIds);
        Assert.True(ids.UsedSetupApi);
        Assert.DoesNotContain(Seagate, ids.HardwareIds);
    }

    [Theory]
    [InlineData(@"USBSTOR\Disk")]
    [InlineData(@"USBSTOR\GenDisk")]
    public void DiskClassCannotRecoverForeignCdRomDevice(string generic)
    {
        var ids = PnpSetupApiIdentity.Merge(
            [generic], [], [CdRom,
                @"PCI\VEN_10DE&DEV_28A0"], [], CdRom, null);
        Assert.Equal([generic], ids.HardwareIds);
        Assert.False(ids.UsedSetupApi);
        Assert.False(ids.UsedSignedDriver);
    }

    [Fact]
    public void MultipleCompatibleClassAliasesPreserveWindowsEvidence()
    {
        var wmi = new[]
        {
            @"USBSTOR\GenDisk", @"USBSTOR\Disk", @"USBSTOR\RAW"
        };
        var ids = PnpSetupApiIdentity.Merge(
            wmi, [], [Seagate, CdRom], [], null, null);
        Assert.Equal([Seagate, .. wmi], ids.HardwareIds);
        Assert.DoesNotContain(CdRom, ids.HardwareIds);
        Assert.True(ids.UsedSetupApi);
    }

    [Fact]
    public void IncompatibleTypesOrBusFailClosed()
    {
        var types = new[] { @"USBSTOR\GenDisk", @"USBSTOR\GenCdRom" };
        Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            types));
        var conflicting = PnpSetupApiIdentity.Merge(
            types, [], [Seagate, CdRom], [], null, null);
        Assert.Equal(types, conflicting.HardwareIds);
        Assert.False(conflicting.UsedSetupApi);

        var mixed = new[] { @"USBSTOR\GenDisk", @"USB\VID_0781" };
        Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            mixed));
        var other = PnpSetupApiIdentity.Merge(
            mixed, [], [Seagate], [], null, null);
        Assert.Equal(mixed, other.HardwareIds);
        Assert.False(other.UsedSetupApi);
    }

    [Theory]
    [InlineData(@"USBSTOR\DiskSEAGATE_")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk")]
    [InlineData(@"USBSTOR\GenDisk&FAKE_01")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_")]
    [InlineData(@"USBSTOR\DiskSEAGATE_ST39102LW_______0004\PRIVATE")]
    [InlineData(@"USBSTOR\UnknownClass")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0")]
    public void VendorOnlyMalformedOrDifferentBusCannotRequestModelRecovery(
        string id)
    {
        Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [id]));
        var ids = PnpSetupApiIdentity.Merge(
            [id], [], [Seagate], [], null, null);
        Assert.Equal([id], ids.HardwareIds);
        Assert.False(ids.UsedSetupApi);
    }

    [Theory]
    [InlineData(Seagate)]
    [InlineData(Samsung)]
    [InlineData(Sandisk)]
    public void AlreadySpecificStorageWmiNeverOverridden(string specific)
    {
        Assert.True(UsbStorPnpMatchPolicy.IsSpecificHardwareId(specific));
        Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [specific]));
        var ids = PnpSetupApiIdentity.Merge(
            [specific], [], [CdRom], [], null, null);
        Assert.Equal([specific], ids.HardwareIds);
        Assert.False(ids.UsedSetupApi);
    }

    [Fact]
    public void MatchingWuaGuidIsEvidenceNotAnAutomaticDriverOffer()
    {
        var ids = PnpSetupApiIdentity.Merge(
            [@"USBSTOR\GenDisk"], [], [Seagate], [], null, null);
        var device = Device(ids);
        var results = DeviceWuaMatchGroups.Create(device, [Wua(Seagate)]);
        Assert.Equal(1, results.TotalCorrelated);
        Assert.Equal(Wua(Seagate).Id, Assert.Single(results.Ordinary).Id);
        Assert.Empty(DeviceWuaMatchGroups.Create(device, []).Ordinary);
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(
            @"USBSTOR\GenDisk"));
    }
}
