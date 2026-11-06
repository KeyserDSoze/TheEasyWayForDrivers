using System.Text.Json;
using System.Text.Json.Nodes;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class PnpIdentifierFallbackTests
{
    private const string NvidiaGpu = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaAudio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string KillerWifi = @"PCI\VEN_8086&DEV_51F1";

    private static DriverInfo GenericDevice(
        PnpIdentifierFallback.Result ids, string deviceId = "LOCAL_PNP_DEVICE") =>
        new(deviceId, "Microsoft Basic Display Adapter", "Microsoft",
            "Display", "Microsoft", "31.0", null, "oem23.inf", true,
            true, 0, ids.HardwareIds, ids.CompatibleIds,
            PnpIdsRecoveredFromSignedDriver: ids.UsedSignedDriverFallback);

    private static DriverUpdateInfo Wua(string hardwareId) =>
        new(Guid.NewGuid().ToString("D"), "Synthetic Windows GPU driver",
            null, "Display", "Microsoft", null, null, false, "Microsoft",
            null, hardwareId, null);

    [Fact]
    public void GenericNvidiaGpuCanCorrelateWuaWhenPnpHardwareArrayIsEmpty()
    {
        var ids = PnpIdentifierFallback.Combine(
            [], [], NvidiaGpu, null);

        Assert.Equal([NvidiaGpu], ids.HardwareIds);
        Assert.Empty(ids.CompatibleIds);
        Assert.True(ids.UsedSignedDriverFallback);
        var driver = GenericDevice(ids);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(driver, Wua(NvidiaGpu)));
        Assert.True(NvidiaHardwareDetector.IsNvidiaDevice(driver));
        var source = Assert.Single(VendorSourceDiscovery.Find([driver], []));
        Assert.Equal("NVIDIA", source.Vendor);
        Assert.Equal(0, source.WuaMatches);
        Assert.False(source.IsVendorApplicable);
    }

    [Fact]
    public void CompatibleOnlyFallbackRemainsWeakerThanExactHardware()
    {
        var ids = PnpIdentifierFallback.Combine([], [], null, NvidiaAudio);
        var driver = GenericDevice(ids);

        Assert.Empty(ids.HardwareIds);
        Assert.Equal([NvidiaAudio], ids.CompatibleIds);
        Assert.True(ids.UsedSignedDriverFallback);
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(driver, Wua(NvidiaAudio)));
    }

    [Fact]
    public void BothMissingListsCanRecoverKillerHardwareAndCompatibleId()
    {
        const string compatible = @"PCI\VEN_8086&DEV_51F1&CC_028000";
        var ids = PnpIdentifierFallback.Combine(
            [], [], KillerWifi, compatible);

        Assert.Equal([KillerWifi], ids.HardwareIds);
        Assert.Equal([compatible], ids.CompatibleIds);
        Assert.True(ids.UsedSignedDriverFallback);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            GenericDevice(ids), Wua(KillerWifi)));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            GenericDevice(ids), Wua(compatible)));
    }

    [Fact]
    public void ExistingPnpIdsAreAuthoritativeAndNeverOverwrittenOrExtended()
    {
        const string pnpHardware = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_12345678";
        const string pnpCompatible = @"PCI\VEN_10DE&DEV_28A0&CC_0300";
        var ids = PnpIdentifierFallback.Combine(
            [pnpHardware], [pnpCompatible], KillerWifi, NvidiaAudio);

        Assert.Equal([pnpHardware], ids.HardwareIds);
        Assert.Equal([pnpCompatible], ids.CompatibleIds);
        Assert.False(ids.UsedSignedDriverFallback);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            GenericDevice(ids), Wua(KillerWifi)));
    }

    [Fact]
    public void EachMissingCategoryCanRecoverWithoutWeakeningExistingPnpRank()
    {
        var ids = PnpIdentifierFallback.Combine(
            [NvidiaGpu], [], KillerWifi, NvidiaAudio);

        Assert.Equal([NvidiaGpu], ids.HardwareIds);
        Assert.Equal([NvidiaAudio], ids.CompatibleIds);
        Assert.True(ids.UsedSignedDriverFallback);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            GenericDevice(ids), Wua(NvidiaGpu)));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            GenericDevice(ids), Wua(NvidiaAudio)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            GenericDevice(ids), Wua(KillerWifi)));
    }

    [Fact]
    public void DuplicateSignedCompatibleIdDoesNotInflateIdentityCount()
    {
        var ids = PnpIdentifierFallback.Combine(
            [], [], NvidiaGpu, NvidiaGpu.ToLowerInvariant());
        Assert.Equal([NvidiaGpu], ids.HardwareIds);
        Assert.Empty(ids.CompatibleIds);
        Assert.True(ids.UsedSignedDriverFallback);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-an-id")]
    [InlineData(@"C:\Windows\System32\driver.sys")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0;USB\VID_046D")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0" + "\n")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0" + "\0")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0/evil")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0..")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\""")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0'")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0" + "\u202E")]
    public void InvalidSignedWmiIdentifiersAreNeverUsed(string raw)
    {
        var ids = PnpIdentifierFallback.Combine([], [], raw, null);
        Assert.Empty(ids.HardwareIds);
        Assert.False(ids.UsedSignedDriverFallback);
    }

    [Fact]
    public void ExcessiveSignedHardwareIdIsRejected()
    {
        var ids = PnpIdentifierFallback.Combine(
            [], [], @"PCI\VEN_" + new string('1', 513), null);
        Assert.Empty(ids.HardwareIds);
        Assert.False(ids.UsedSignedDriverFallback);
    }

    [Fact]
    public void NearMissVendorPrefixIsNotAttributedToNvidia()
    {
        var ids = PnpIdentifierFallback.Combine(
            [], [], @"PCI\VEN_10DE0&DEV_28A0", null);
        Assert.True(ids.UsedSignedDriverFallback);
        var device = GenericDevice(ids);
        Assert.False(NvidiaHardwareDetector.IsNvidiaDevice(device));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, Wua(NvidiaGpu)));
    }

    [Fact]
    public void CorrelatedWuaUpdateIsNotFabricatedWhenNoIdsWereRecovered()
    {
        var ids = PnpIdentifierFallback.Combine(
            [], [], "bad missing ID", "not-an-id");
        Assert.False(ids.UsedSignedDriverFallback);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(GenericDevice(ids),
            Wua(NvidiaGpu)));
    }

    [Fact]
    public void ScanCoverageCountsOnlyRecoveredDevicesWithoutPublishingPrivateIds()
    {
        var gpu = GenericDevice(PnpIdentifierFallback.Combine(
            [], [], NvidiaGpu, null), @"PCI\VEN_10DE&DEV_28A0\PRIVATE-SERIAL");
        var audio = GenericDevice(PnpIdentifierFallback.Combine(
            [], [], null, NvidiaAudio), @"HDAUDIO\FUNC_01&VEN_10DE\PRIVATE-AUDIO");

        var output = ScanCoverageText.Create(null, [], 2, 0, 0,
            [gpu, audio], inventoryComplete: true);

        Assert.Contains("Identificativi PnP integrati da metadati driver locali: 2", output);
        Assert.Contains("NON conferma nuovi aggiornamenti", output);
        Assert.DoesNotContain("PRIVATE-SERIAL", output);
        Assert.DoesNotContain("PRIVATE-AUDIO", output);
        Assert.DoesNotContain(NvidiaGpu, output);
        Assert.DoesNotContain(NvidiaAudio, output);
        Assert.DoesNotContain("Microsoft Basic Display Adapter", output);
    }

    [Fact]
    public void NewIpcFlagIsBackwardsCompatibleAndRoundTrips()
    {
        var input = GenericDevice(PnpIdentifierFallback.Combine(
            [], [], NvidiaGpu, null));

        var serialized = JsonSerializer.Serialize(input);
        var copy = JsonSerializer.Deserialize<DriverInfo>(serialized);
        Assert.NotNull(copy);
        Assert.True(copy.PnpIdsRecoveredFromSignedDriver);

        var oldJson = JsonNode.Parse(serialized)!.AsObject();
        Assert.True(oldJson.Remove(nameof(DriverInfo.PnpIdsRecoveredFromSignedDriver)));
        var legacy = JsonSerializer.Deserialize<DriverInfo>(oldJson.ToJsonString());
        Assert.NotNull(legacy);
        Assert.False(legacy.PnpIdsRecoveredFromSignedDriver);
        Assert.Equal([NvidiaGpu], legacy.HardwareIds);
    }

    [Fact]
    public void UnavailableSignedDriverMetadataCannotSupplyAnotherDevicesIds()
    {
        var nvidia = PnpIdentifierFallback.Combine([], [], null, null);
        var existing = PnpIdentifierFallback.Combine(
            [KillerWifi], [], null, null);
        Assert.Empty(nvidia.HardwareIds);
        Assert.False(nvidia.UsedSignedDriverFallback);
        Assert.Equal([KillerWifi], existing.HardwareIds);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            GenericDevice(nvidia), Wua(KillerWifi)));
    }
}
