using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A WMI PCI class-only node is one device, not an opportunity to attach
/// two different native VEN identities from an inconsistent SetupAPI read.
/// Same-instance recovery remains read-only and bounded.
/// </summary>
public sealed class PnpPciNativeVendorConflictTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaSubsys =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"PCI\VEN_10EC&DEV_8168";
    private const string DisplayClass = @"PCI\CC_0300";
    private const string NetworkClass = @"PCI\CC_0280";
    private const string NvidiaVendorOnly = @"PCI\VEN_10DE&CC_0300";

    private static DriverInfo Device(
        PnpSetupApiIdentity.Enrichment evidence) =>
        new(@"PCI\VEN_0000\PRIVATE_INSTANCE_SERIAL", "PCI Device",
            "Microsoft", "Unknown", "Microsoft", "1.0", null,
            "oem12.inf", true, true, 0,
            evidence.HardwareIds, evidence.CompatibleIds,
            PnpIdsRecoveredFromSetupApi: evidence.UsedSetupApi,
            PnpIdsRecoveredFromSignedDriver: evidence.UsedSignedDriver);

    private static DriverUpdateInfo Wua(string hardware) =>
        new("00000000-0000-4000-8000-000000000369",
            "Windows device driver", null, "System", "Microsoft",
            null, null, false, "Microsoft", null, hardware, null,
            SearchSource: "Windows Update online");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AmbiguousNativePciVendorsCannotUpgradeClassOnlyWmiToTwoDevices(
        bool nvidiaFirst)
    {
        var native = nvidiaFirst
            ? new[] { NvidiaSubsys, Killer, Nvidia }
            : [Killer, NvidiaSubsys, Nvidia];
        var result = PnpSetupApiIdentity.Merge(
            [DisplayClass], [], native, [Killer, Nvidia],
            Realtek, Killer);
        Assert.Equal([DisplayClass], result.HardwareIds);
        Assert.Empty(result.CompatibleIds);
        Assert.False(result.UsedSetupApi);
        Assert.False(result.UsedSignedDriver);
        var device = Device(result);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, Wua(Nvidia)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, Wua(Killer)));
        Assert.Null(PnpKnownVendorIdentity.FromDriver(device));
        Assert.Null(VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        var report = ScanCoverageText.Create(null, [], 1, 0, 0,
            [device], inventoryComplete: true);
        Assert.DoesNotContain("PRIVATE_INSTANCE", report);
        Assert.DoesNotContain(Nvidia, report);
        Assert.DoesNotContain(Killer, report);
    }

    [Fact]
    public void ConsistentNativeNvidiaModelsStillRecoverAndDropForeignCompatible()
    {
        var result = PnpSetupApiIdentity.Merge(
            [DisplayClass], [],
            [NvidiaSubsys, Nvidia],
            [Nvidia, Killer, DisplayClass,
                @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295"],
            Killer, Killer);

        Assert.Equal(NvidiaSubsys, result.HardwareIds[0]);
        Assert.Contains(Nvidia, result.HardwareIds);
        Assert.Contains(DisplayClass, result.HardwareIds);
        Assert.Contains(Nvidia, result.CompatibleIds);
        Assert.Contains(DisplayClass, result.CompatibleIds);
        Assert.DoesNotContain(Killer, result.HardwareIds);
        Assert.DoesNotContain(Killer, result.CompatibleIds);
        Assert.DoesNotContain(result.CompatibleIds,
            id => id.StartsWith(@"HDAUDIO\", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.UsedSetupApi);
        Assert.False(result.UsedSignedDriver);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(result), Wua(NvidiaSubsys)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(result), Wua(Killer)));
    }

    [Fact]
    public void WmiVendorOnlyNvidiaConstraintPreservesMatchingNativeVendor()
    {
        var result = PnpSetupApiIdentity.Merge(
            [NvidiaVendorOnly], [],
            [Nvidia, Killer],
            [Killer, Nvidia, DisplayClass],
            Killer, Killer);

        Assert.Equal(Nvidia, result.HardwareIds[0]);
        Assert.Contains(NvidiaVendorOnly, result.HardwareIds);
        Assert.DoesNotContain(Killer, result.HardwareIds);
        Assert.DoesNotContain(Killer, result.CompatibleIds);
        Assert.Contains(Nvidia, result.CompatibleIds);
        Assert.Contains(DisplayClass, result.CompatibleIds);
        Assert.True(result.UsedSetupApi);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(result), Wua(Nvidia)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(result), Wua(Killer)));
    }

    [Fact]
    public void NoSpecificNativeVendorCannotPromoteForeignCompatibleOrSignedId()
    {
        var result = PnpSetupApiIdentity.Merge(
            [NetworkClass], [], [],
            [Killer, Realtek, NetworkClass,
                @"USB\VID_8087&PID_0033"], null, Realtek);
        Assert.Equal([NetworkClass], result.HardwareIds);
        Assert.Equal([NetworkClass], result.CompatibleIds);
        Assert.True(result.UsedSetupApi);
        Assert.False(result.UsedSignedDriver);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(result), Wua(Killer)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(result), Wua(Realtek)));
    }

    [Fact]
    public void IncompleteWmiVendorCanStillUseConsistentSignedCompatible()
    {
        var result = PnpSetupApiIdentity.Merge(
            [NvidiaVendorOnly], [], [], [], null, Nvidia);
        Assert.Equal([NvidiaVendorOnly], result.HardwareIds);
        Assert.Equal([Nvidia], result.CompatibleIds);
        Assert.False(result.UsedSetupApi);
        Assert.True(result.UsedSignedDriver);
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            Device(result), Wua(Nvidia)));
    }

    [Fact]
    public void WmiAlreadyHasSpecificHardwareAndCompatibleKeepsItsPrecedence()
    {
        var result = PnpSetupApiIdentity.Merge(
            [NvidiaSubsys], [Nvidia],
            [Killer], [Killer], Killer, Killer);
        Assert.Equal([NvidiaSubsys], result.HardwareIds);
        Assert.Equal([Nvidia], result.CompatibleIds);
        Assert.False(result.UsedSetupApi);
        Assert.False(result.UsedSignedDriver);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(result), Wua(Killer)));
    }
}
