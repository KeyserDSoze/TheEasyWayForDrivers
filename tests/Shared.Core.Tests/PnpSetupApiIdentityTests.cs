using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class PnpSetupApiIdentityTests
{
    private const string GpuSpecific =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string GpuGeneric = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer =
        @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static byte[] MultiSz(params string[] ids) =>
        Encoding.Unicode.GetBytes(string.Join('\0', ids) + "\0\0");

    private static DriverInfo WithIdentity(
        PnpSetupApiIdentity.Enrichment identity) =>
        new(@"PCI\VEN_10DE&DEV_28A0\PRIVATE-INSTANCE",
            "Microsoft Basic Display Adapter", "Microsoft",
            "Display", "Microsoft", "31.0.0.0", null, "oem1.inf",
            true, true, 0,
            identity.HardwareIds, identity.CompatibleIds,
            PnpIdsRecoveredFromSignedDriver: identity.UsedSignedDriver,
            PnpIdsRecoveredFromSetupApi: identity.UsedSetupApi);

    private static DriverUpdateInfo Wua(string id) =>
        new(Guid.NewGuid().ToString("D"), "NVIDIA - Display",
            null, "Display", "Microsoft", null, 1000, false,
            "NVIDIA", "RTX 4060", id, null);

    [Fact]
    public void StrictWindowsMultiSzDecodesOrderedNvidiaKillerRealtekAndCasefoldsDuplicates()
    {
        var bytes = MultiSz(GpuSpecific, GpuGeneric, Killer,
            Realtek, GpuSpecific.ToLowerInvariant());
        var result = PnpSetupApiIdentity.Decode(bytes,
            PnpSetupApiIdentity.RegMultiSz, (uint)bytes.Length);
        Assert.Equal([GpuSpecific, GpuGeneric, Killer, Realtek], result);
    }

    [Fact]
    public void NativeHardwareIdsEnableActualWuaGpuCorrelationWithGenericName()
    {
        var raw = MultiSz(GpuSpecific, GpuGeneric);
        var native = PnpSetupApiIdentity.Decode(raw, 7, (uint)raw.Length);
        var identity = PnpSetupApiIdentity.Merge(
            [], [], native, [GpuGeneric], null, null);
        var driver = WithIdentity(identity);

        Assert.True(identity.UsedSetupApi);
        Assert.False(identity.UsedSignedDriver);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(driver, Wua(GpuSpecific)));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(driver, Wua(GpuGeneric)));
        Assert.True(NvidiaHardwareDetector.IsNvidiaDevice(driver));
        Assert.True(driver.PnpIdsRecoveredFromSetupApi);
        Assert.False(driver.PnpIdsRecoveredFromSignedDriver);
    }

    [Fact]
    public void ExistingWmiIdsAlwaysBeatDifferentNativeAndSignedIds()
    {
        var identity = PnpSetupApiIdentity.Merge(
            [GpuSpecific], [GpuGeneric],
            [Killer], [Realtek], Killer, Realtek);
        Assert.Equal([GpuSpecific], identity.HardwareIds);
        Assert.Equal([GpuGeneric], identity.CompatibleIds);
        Assert.False(identity.UsedSetupApi);
        Assert.False(identity.UsedSignedDriver);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            WithIdentity(identity), Wua(Killer)));
    }

    [Fact]
    public void MissingWmiCompatibleIdsUseNativeAndKeepNativeHardwareIndependent()
    {
        var identity = PnpSetupApiIdentity.Merge(
            [GpuSpecific], [], [Killer],
            [GpuGeneric], Killer, Realtek);
        Assert.Equal([GpuSpecific], identity.HardwareIds);
        Assert.Equal([GpuGeneric], identity.CompatibleIds);
        Assert.True(identity.UsedSetupApi);
        Assert.False(identity.UsedSignedDriver);
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            WithIdentity(identity), Wua(GpuGeneric)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            WithIdentity(identity), Wua(Killer)));
    }

    [Fact]
    public void SignedDriverCanFillOnlyStillMissingCategoryAfterNative()
    {
        var identity = PnpSetupApiIdentity.Merge(
            [], [], [GpuSpecific], [], Killer, GpuGeneric);
        Assert.Equal([GpuSpecific], identity.HardwareIds);
        Assert.Equal([GpuGeneric], identity.CompatibleIds);
        Assert.True(identity.UsedSetupApi);
        Assert.True(identity.UsedSignedDriver);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            WithIdentity(identity), Wua(GpuSpecific)));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            WithIdentity(identity), Wua(GpuGeneric)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            WithIdentity(identity), Wua(Killer)));
    }

    [Fact]
    public void InvalidNativeDataFallsBackToSignedWithoutFabricatingOtherDevices()
    {
        var raw = MultiSz("C:\\Users\\PRIVATE\\driver.inf",
            "PCI\\VEN_10DE&DEV_28A0\nINJECTION",
            "PCI\\VEN_10DE&DEV_28A0;USB\\VID_0001");
        var invalid = PnpSetupApiIdentity.Decode(raw, 7, (uint)raw.Length);
        Assert.Empty(invalid);
        var identity = PnpSetupApiIdentity.Merge(
            [], [], invalid, invalid, Killer, null);
        Assert.Equal([Killer], identity.HardwareIds);
        Assert.True(identity.UsedSignedDriver);
        Assert.False(identity.UsedSetupApi);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            WithIdentity(identity), Wua(GpuSpecific)));
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(0u)]
    [InlineData(3u)]
    public void NonMultiSzRegistryPropertyTypeMustNotSupplyHardwareIds(uint type)
    {
        var raw = MultiSz(GpuSpecific);
        Assert.Empty(PnpSetupApiIdentity.Decode(raw, type,
            (uint)raw.Length));
    }

    [Fact]
    public void MissingDoubleTerminatorOddLengthOrTruncatedArrayCannotSupplyIds()
    {
        var valid = MultiSz(GpuSpecific);
        Assert.Empty(PnpSetupApiIdentity.Decode(valid, 7,
            (uint)valid.Length - 2));
        Assert.Empty(PnpSetupApiIdentity.Decode(valid, 7,
            (uint)valid.Length - 1));
        Assert.Empty(PnpSetupApiIdentity.Decode(valid, 7,
            (uint)valid.Length + 2));

        var oneTerminator = Encoding.Unicode.GetBytes(GpuSpecific + "\0");
        Assert.Empty(PnpSetupApiIdentity.Decode(oneTerminator, 7,
            (uint)oneTerminator.Length));
    }

    [Fact]
    public void OversizedRegistryDataAndOver128EntriesRejectEntireProperty()
    {
        var oversized = new byte[PnpSetupApiIdentity.MaximumBytes + 4];
        Assert.Empty(PnpSetupApiIdentity.Decode(oversized, 7,
            (uint)oversized.Length));
        var many = MultiSz(Enumerable.Range(0, 129)
            .Select(i => $@"PCI\VEN_10DE&DEV_{i:X4}").ToArray());
        Assert.Empty(PnpSetupApiIdentity.Decode(many, 7,
            (uint)many.Length));
    }

    [Fact]
    public void BrokenUtf16SurrogateIsRejectedInsteadOfChangingId()
    {
        var valid = MultiSz(GpuSpecific);
        // High surrogate unpaired in the first character.
        valid[0] = 0x00;
        valid[1] = 0xD8;
        Assert.Empty(PnpSetupApiIdentity.Decode(valid, 7,
            (uint)valid.Length));
    }

    [Fact]
    public void SafeNativeIdValidatorRejectsPathsBidiWhitespaceAndMissingBus()
    {
        Assert.True(PnpIdentifierFallback.IsStrictSingleId(GpuSpecific));
        Assert.True(PnpIdentifierFallback.IsStrictSingleId(Realtek));
        Assert.False(PnpIdentifierFallback.IsStrictSingleId(
            " " + GpuSpecific));
        Assert.False(PnpIdentifierFallback.IsStrictSingleId(
            GpuSpecific + "\u202E"));
        Assert.False(PnpIdentifierFallback.IsStrictSingleId(
            "C:\\Users\\PRIVATE\\driver.inf"));
        Assert.False(PnpIdentifierFallback.IsStrictSingleId(
            "NVIDIA GeForce 4060"));
    }

    [Fact]
    public void SetupApiFlagIsOptionalAndBackwardsCompatibleWithOlderIpc()
    {
        var identity = PnpSetupApiIdentity.Merge(
            [], [], [GpuSpecific], [], null, null);
        var driver = WithIdentity(identity);
        var serialized = JsonSerializer.Serialize(driver);
        var copy = JsonSerializer.Deserialize<DriverInfo>(serialized);
        Assert.NotNull(copy);
        Assert.True(copy.PnpIdsRecoveredFromSetupApi);
        Assert.Equal([GpuSpecific], copy.HardwareIds);

        var oldJson = JsonNode.Parse(serialized)!.AsObject();
        Assert.True(oldJson.Remove(nameof(DriverInfo.PnpIdsRecoveredFromSetupApi)));
        var old = JsonSerializer.Deserialize<DriverInfo>(oldJson.ToJsonString());
        Assert.NotNull(old);
        Assert.False(old.PnpIdsRecoveredFromSetupApi);
        Assert.Equal([GpuSpecific], old.HardwareIds);
    }

    [Fact]
    public void AnonymousCoverageReportCountsNativeRecoveryWithoutLeakingIds()
    {
        var identity = PnpSetupApiIdentity.Merge(
            [], [], [GpuSpecific], [GpuGeneric], null, null);
        var driver = WithIdentity(identity);
        var report = ScanCoverageText.Create(null, [], 1, 0, 0,
            [driver], inventoryComplete: true);
        Assert.Contains("SetupAPI Windows locale: 1 dispositivi", report);
        Assert.Contains("NON significa driver nuovi o installabili", report);
        Assert.DoesNotContain("PRIVATE-INSTANCE", report);
        Assert.DoesNotContain(GpuSpecific, report);
        Assert.DoesNotContain(GpuGeneric, report);
        Assert.DoesNotContain("Microsoft Basic Display Adapter", report);
    }
}
