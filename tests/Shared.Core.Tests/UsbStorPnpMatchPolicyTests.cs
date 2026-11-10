using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class UsbStorPnpMatchPolicyTests
{
    // Microsoft USBSTOR.SYS examples: fixed v(8), p(16) and optional r(4).
    private const string Seagate =
        @"USBSTOR\DiskSEAGATE_ST39102LW_______0004";
    private const string Samsung =
        @"USBSTOR\DiskSAMSUNG_SSD_980_________0001";

    private static DriverInfo Device(string hardware,
        IReadOnlyList<string>? compatible = null) =>
        new("PRIVATE_DISK_SERIAL", "Disco USB generico", "Microsoft",
            "DiskDrive", "Microsoft", "1.0", null, "disk.inf", true,
            true, 0, [hardware], compatible ?? []);

    private static DriverUpdateInfo Offer(string id, int sequence = 1) =>
        new($"00000000-0000-4000-8000-{sequence:D12}",
            "Windows storage driver", null, "DiskDrive", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    private static string Inf(string hardwareId) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\n%Maker%=Models,NTamd64\n" +
        "[Models.NTamd64]\n%Storage%=Install," + hardwareId;

    [Theory]
    [InlineData(@"USBSTOR\GenDisk")]
    [InlineData(@"USBSTOR\GenCdRom")]
    [InlineData(@"USBSTOR\GenSFloppy")]
    [InlineData(@"USBSTOR\RAW")]
    [InlineData(@"USBSTOR\Disk")]
    [InlineData(@"USBSTOR\CdRom")]
    [InlineData(@"USBSTOR\UsbstorOther")]
    [InlineData(@"USBSTOR\DiskSEAGATE_")]
    [InlineData(@"USBSTOR\DiskSEAGATE_ST39102LW______")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer&Fake_01")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer&Rev_")]
    [InlineData(@"USBSTOR\DiskSEAGATE_ST39102LW_______0004\INSTANCE")]
    [InlineData(@"USBSTOR\DiskSEAGATE_ST39102LW_______000G")]
    public void SharedOrMalformedStorageIdDoesNotCreateDeviceMatch(string id)
    {
        Assert.False(UsbStorPnpMatchPolicy.IsSpecificHardwareId(id));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(id));
        var device = Device(id);
        var offer = Offer(id);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, offer));
        var grouped = DeviceWuaMatchGroups.Create(device, [offer]);
        Assert.Empty(grouped.Ordinary);
        Assert.Empty(grouped.ReviewOnly);
        Assert.Empty(grouped.ModelOnlyReview!);
        Assert.Equal(0, InfModelEvidence.MatchStrength(device,
            InfModelEvidence.ReadDeclaredIds(Inf(id))));
        Assert.Empty(InfModelEvidence.ReadMatchingIds(Inf(id), [device]));
    }

    [Theory]
    [InlineData(Seagate)]
    [InlineData(@"USBSTOR\DiskSEAGATE_ST39102LW_______")]
    [InlineData(@"USBSTOR\SEAGATE_ST39102LW_______0")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_Blade")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_Blade&Rev_1.00")]
    public void SpecificStorageIdsStillCorrelateExactly(string id)
    {
        Assert.True(UsbStorPnpMatchPolicy.IsSpecificHardwareId(id));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(id));
        var offer = Offer(id);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(Device(id), offer));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            Device(Samsung, [id]), offer));
        Assert.Equal(1, DeviceWuaMatchGroups.Create(
            Device(id), [offer]).TotalCorrelated);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(Samsung), offer));
    }

    [Fact]
    public void VendorOrClassCompatibleIdsCannotOutrankExactSeagateProduct()
    {
        const string shared = @"USBSTOR\GenDisk";
        var device = Device(Seagate, [shared, @"USBSTOR\DiskSEAGATE_"]);
        var genericOffer = Offer(shared);
        var productOffer = Offer(Seagate, 2);
        var found = DriverUpdateMatcher.FindMatches(
            device, [genericOffer, productOffer]);
        Assert.Equal(productOffer.Id, Assert.Single(found).Id);

        Assert.Equal(2, InfModelEvidence.MatchStrength(device,
            InfModelEvidence.ReadDeclaredIds(Inf(Seagate))));
        Assert.Empty(InfModelEvidence.ReadMatchingIds(
            Inf(shared), [device]));
    }

    [Fact]
    public void GenericStorageGuidRemainsUnattributedInAnonymousAudit()
    {
        const string generic = @"USBSTOR\GenDisk";
        var report = new DriverSearchReport([Offer(generic)],
        [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "PRIVATE_PC_LOCATION")
        ]);
        var audit = WuaPnpAssociationAudit.Create(
            [Device(generic)], report, true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(0, audit.CompatibleOnly);
        Assert.Equal(0, audit.GenericModelOnly);
        Assert.Equal(1, audit.Unattributed);
        Assert.False(audit.IsInstallable);
        Assert.DoesNotContain("PRIVATE", audit.Text);
        Assert.DoesNotContain(generic, audit.Text);
    }

    [Fact]
    public void OtherSpecificBusIdsAndUpdateAllPolicyAreUnaffected()
    {
        const string usb = @"USB\VID_0781&PID_5567";
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(usb));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(
            @"PCI\VEN_10DE&DEV_28A0"));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(
            @"HID\VID_046D&PID_C534"));
        var ordinaryWua = Offer(Seagate);
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([ordinaryWua]));
        Assert.False(ordinaryWua.IsAdvancedCandidate);
    }
}
