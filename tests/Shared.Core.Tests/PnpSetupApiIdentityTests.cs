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
    public void MalformedButNonemptyWmiHardwareAndCompatibleEnableExactNativeRecovery()
    {
        var badWmiHardware = new[]
        {
            @"C:\Users\PRIVATE\driver.sys",
            @"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE",
            @"PCI\VEN_10DE&DEV_28A0" + "\u202E"
        };
        var badWmiCompatible = new[] { "not a PnP ID" };

        Assert.Empty(PnpSetupApiIdentity.SanitizeWmiIds(badWmiHardware));
        Assert.Empty(PnpSetupApiIdentity.SanitizeWmiIds(badWmiCompatible));
        var identity = PnpSetupApiIdentity.Merge(
            badWmiHardware, badWmiCompatible,
            [GpuSpecific], [GpuGeneric], Killer, Realtek);
        var driver = WithIdentity(identity);

        Assert.Equal([GpuSpecific], identity.HardwareIds);
        Assert.Equal([GpuGeneric], identity.CompatibleIds);
        Assert.True(identity.UsedSetupApi);
        Assert.False(identity.UsedSignedDriver);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            driver, Wua(GpuSpecific)));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            driver, Wua(GpuGeneric)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            driver, Wua(Killer)));
        Assert.True(NvidiaHardwareDetector.IsNvidiaDevice(driver));
        Assert.DoesNotContain(identity.HardwareIds,
            id => id.Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidWmiIdsStayAuthoritativeAndInvalidEntriesCannotBlockOtherCategory()
    {
        var sanitized = PnpSetupApiIdentity.SanitizeWmiIds([
            GpuSpecific,
            GpuSpecific.ToLowerInvariant(),
            @"C:\PRIVATE\setup.exe",
            "PCI\\VEN_10DE&DEV_28A0\u202E",
            GpuGeneric
        ]);
        Assert.Equal([GpuSpecific, GpuGeneric], sanitized);
        var identity = PnpSetupApiIdentity.Merge(
            [GpuSpecific, "untrusted path"],
            ["not a compatible id"],
            [Killer], [GpuGeneric], Killer, Realtek);
        Assert.Equal([GpuSpecific], identity.HardwareIds);
        Assert.Equal([GpuGeneric], identity.CompatibleIds);
        Assert.True(identity.UsedSetupApi);
        Assert.False(identity.UsedSignedDriver);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            WithIdentity(identity), Wua(Killer)));
    }

    [Fact]
    public void NativeFallbackRecoversKillerAndRealtekFromUnusableWmiLists()
    {
        var killer = PnpSetupApiIdentity.Merge(
            ["malformed"], ["bad"],
            [Killer], [], GpuSpecific, Realtek);
        Assert.Equal([Killer], killer.HardwareIds);
        Assert.Equal([Realtek], killer.CompatibleIds);
        Assert.True(killer.UsedSetupApi);
        Assert.True(killer.UsedSignedDriver);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            WithIdentity(killer), Wua(Killer)));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            WithIdentity(killer), Wua(Realtek)));

        var audio = PnpSetupApiIdentity.Merge(
            ["invalid vendor text"], [],
            [Realtek], [], GpuSpecific, null);
        Assert.Equal([Realtek], audio.HardwareIds);
        Assert.True(audio.UsedSetupApi);
        Assert.False(audio.UsedSignedDriver);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            WithIdentity(audio), Wua(Realtek)));
    }

    [Fact]
    public void TooManyWmiEntriesMustNotSilentlyClaimAnExhaustiveIdentityList()
    {
        var overBudget = Enumerable.Repeat(
            GpuSpecific, PnpSetupApiIdentity.MaximumIds + 1).ToArray();
        Assert.Empty(PnpSetupApiIdentity.SanitizeWmiIds(overBudget));
        var identity = PnpSetupApiIdentity.Merge(
            overBudget, [], [GpuGeneric], [], Killer, null);
        Assert.Equal([GpuGeneric], identity.HardwareIds);
        Assert.True(identity.UsedSetupApi);
        Assert.False(identity.UsedSignedDriver);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            WithIdentity(identity), Wua(GpuSpecific)));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            WithIdentity(identity), Wua(GpuGeneric)));
    }

    [Fact]
    public void MissingNativeAfterMalformedWmiFallsBackOnlyToSameInstanceSignedMetadata()
    {
        var identity = PnpSetupApiIdentity.Merge(
            [@"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE"], [],
            [], [], GpuSpecific, null);
        Assert.Equal([GpuSpecific], identity.HardwareIds);
        Assert.True(identity.UsedSignedDriver);
        Assert.False(identity.UsedSetupApi);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            WithIdentity(identity), Wua(GpuSpecific)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            WithIdentity(identity), Wua(Killer)));
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

    [Theory]
    [InlineData(@"PCI\CC_030000")]
    [InlineData(@"PCI\CC_0300")]
    [InlineData(@"PCI\CC_028000")]
    public void ClassOnlyWmiPciIdentityNeedsExactInstanceModelRecovery(
        string classCode)
    {
        Assert.True(PnpSetupApiIdentity.NeedsPciHardwareModelRecovery(
            [classCode]));
        var recovered = PnpSetupApiIdentity.Merge(
            [classCode], [@"PCI\CC_0300"],
            [GpuSpecific, GpuGeneric], [Killer], null, null);

        Assert.Equal(GpuSpecific, recovered.HardwareIds[0]);
        Assert.Contains(classCode, recovered.HardwareIds);
        Assert.Equal([@"PCI\CC_0300"], recovered.CompatibleIds);
        Assert.True(recovered.UsedSetupApi);
        Assert.False(recovered.UsedSignedDriver);
        var device = WithIdentity(recovered);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            device, Wua(GpuSpecific)));
        Assert.True(NvidiaHardwareDetector.IsNvidiaDevice(device));
        var privacy = ScanCoverageText.Create(null, [], 1, 0, 0,
            [device], inventoryComplete: true);
        Assert.Contains("SetupAPI Windows locale: 1 dispositivi", privacy);
        Assert.DoesNotContain(GpuSpecific, privacy);
        Assert.DoesNotContain(classCode, privacy);
        Assert.DoesNotContain("PRIVATE", privacy);
    }

    [Fact]
    public void ClassOnlyPciNetworkCanRecoverActualKillerModel()
    {
        var identity = PnpSetupApiIdentity.Merge(
            [@"PCI\CC_0280"], [], [Killer], [], null, null);
        Assert.Equal(Killer, identity.HardwareIds[0]);
        Assert.True(identity.UsedSetupApi);
        var device = WithIdentity(identity) with
        {
            Name = "Network Controller",
            DeviceClass = "Unknown",
            Manufacturer = "Microsoft",
            DriverProvider = "Microsoft",
            CompatibleIds = [@"PCI\CC_0280"]
        };
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(device, Wua(Killer)));
        Assert.Equal("Intel",
            VendorSourceDiscovery.GetRecognizedSupportVendor(device));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE")]
    [InlineData(@"PCI\VEN_10DE&FAKE_28A0")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE-INSTANCE")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A")]
    [InlineData(@"USB\VID_8087&PID_0033")]
    [InlineData(@"PCI\CC_0300")]
    public void UnverifiedNativeIdCannotReplaceWmiClassOnlyEvidence(
        string native)
    {
        var original = new[] { @"PCI\CC_030000" };
        var recovered = PnpSetupApiIdentity.Merge(
            original, [@"PCI\CC_0300"], [native], [], null, null);
        Assert.Equal(original, recovered.HardwareIds);
        Assert.False(recovered.UsedSetupApi);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            WithIdentity(recovered), Wua(GpuSpecific)));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295")]
    [InlineData(@"USB\VID_8087&PID_0033")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA")]
    [InlineData(@"PCI\CC_030000&FAKE_0001")]
    public void CompleteOrNonClassOnlyWmiIdentityNeverOverridden(string wmiId)
    {
        Assert.False(PnpSetupApiIdentity.NeedsPciHardwareModelRecovery([wmiId]));
        var result = PnpSetupApiIdentity.Merge(
            [wmiId], [@"PCI\CC_0300"], [Killer], [], null, null);
        Assert.Equal([wmiId], result.HardwareIds);
        Assert.False(result.UsedSetupApi);
        Assert.False(result.UsedSignedDriver);
    }

    [Fact]
    public void MixedRealHardwareWithClassCodesDoesNotDiscardValidWindowsIds()
    {
        var mixed = new[] { @"PCI\CC_030000", GpuSpecific };
        Assert.False(PnpSetupApiIdentity.NeedsPciHardwareModelRecovery(mixed));
        var ids = PnpSetupApiIdentity.Merge(
            mixed, [], [Killer], [], null, null);
        Assert.Equal(mixed, ids.HardwareIds);
        Assert.False(ids.UsedSetupApi);
    }


    [Theory]
    [InlineData(@"PCI\VEN_10DE")]
    [InlineData(@"PCI\VEN_10DE&CC_0300")]
    [InlineData(@"PCI\VEN_10DE&CC_030000")]
    [InlineData(@"pci\ven_10de&cc_030000")]
    public void VendorOnlyOrVendorClassWmiRecoversSameVendorNvidiaGpu(
        string incompleteId)
    {
        Assert.True(PnpSetupApiIdentity.NeedsPciHardwareModelRecovery(
            [incompleteId]));
        var recovered = PnpSetupApiIdentity.Merge(
            [incompleteId], [@"PCI\CC_0300"],
            [GpuSpecific, GpuGeneric, Killer], [], null, null);

        Assert.Equal(GpuSpecific, recovered.HardwareIds[0]);
        Assert.Contains(GpuGeneric, recovered.HardwareIds);
        Assert.Contains(incompleteId, recovered.HardwareIds);
        Assert.DoesNotContain(Killer, recovered.HardwareIds);
        Assert.Equal([@"PCI\CC_0300"], recovered.CompatibleIds);
        Assert.True(recovered.UsedSetupApi);
        Assert.False(recovered.UsedSignedDriver);
        var device = WithIdentity(recovered);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            device, Wua(GpuSpecific)));
        Assert.True(NvidiaHardwareDetector.IsNvidiaDevice(device));

        var coverage = ScanCoverageText.Create(null, [], 1, 0, 0,
            [device], inventoryComplete: true);
        Assert.Contains("SetupAPI Windows locale: 1 dispositivi", coverage);
        Assert.DoesNotContain(incompleteId, coverage);
        Assert.DoesNotContain(GpuSpecific, coverage);
        Assert.DoesNotContain("PRIVATE", coverage);
    }

    [Theory]
    [InlineData(@"PCI\VEN_8086")]
    [InlineData(@"PCI\VEN_8086&CC_0280")]
    [InlineData(@"PCI\VEN_8086&CC_028000")]
    public void IntelVendorOnlyWmiRecoversSameDeviceKillerWifi(
        string incompleteId)
    {
        var recovered = PnpSetupApiIdentity.Merge(
            [incompleteId, @"PCI\CC_0280"], [],
            [Killer, GpuSpecific], [], GpuSpecific, null);
        Assert.True(recovered.UsedSetupApi);
        Assert.False(recovered.UsedSignedDriver);
        Assert.Equal(Killer, recovered.HardwareIds[0]);
        Assert.Contains(incompleteId, recovered.HardwareIds);
        Assert.DoesNotContain(GpuSpecific, recovered.HardwareIds);
        var device = WithIdentity(recovered) with
        {
            Name = "Network Controller",
            DeviceClass = "Unknown"
        };
        Assert.Equal("Intel",
            VendorSourceDiscovery.GetRecognizedSupportVendor(device));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(device, Wua(Killer)));
    }

    [Fact]
    public void CompleteModelWinsAgainstVendorClassNativeOrSignedMismatch()
    {
        var wmi = new[]
        {
            @"PCI\VEN_10DE&CC_030000", GpuSpecific
        };
        Assert.False(PnpSetupApiIdentity.NeedsPciHardwareModelRecovery(wmi));
        var result = PnpSetupApiIdentity.Merge(
            wmi, [GpuGeneric], [Killer], [Realtek], Killer, Realtek);
        Assert.Equal(wmi, result.HardwareIds);
        Assert.Equal([GpuGeneric], result.CompatibleIds);
        Assert.False(result.UsedSetupApi);
        Assert.False(result.UsedSignedDriver);
    }

    [Fact]
    public void ForeignOrConflictingVendorCannotReplacePartialWmiHardware()
    {
        const string nvidiaOnly = @"PCI\VEN_10DE&CC_0300";
        var wrongNative = PnpSetupApiIdentity.Merge(
            [nvidiaOnly], [], [Killer], [], null, null);
        Assert.Equal([nvidiaOnly], wrongNative.HardwareIds);
        Assert.False(wrongNative.UsedSetupApi);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            WithIdentity(wrongNative), Wua(Killer)));

        var ambiguous = new[]
        {
            @"PCI\VEN_10DE", @"PCI\VEN_8086&CC_0300"
        };
        Assert.False(PnpSetupApiIdentity.NeedsPciHardwareModelRecovery(ambiguous));
        var conflicting = PnpSetupApiIdentity.Merge(
            ambiguous, [], [GpuSpecific, Killer], [], null, null);
        Assert.Equal(ambiguous, conflicting.HardwareIds);
        Assert.False(conflicting.UsedSetupApi);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&CC_03")]
    [InlineData(@"PCI\VEN_10DE&CC_03000")]
    [InlineData(@"PCI\VEN_10DE&CC_0300000")]
    [InlineData(@"PCI\VEN_10DE&FAKE_0300")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&FAKE_0000")]
    [InlineData(@"PCI\VEN_10D")]
    [InlineData(@"USB\VID_8087&PID_0033")]
    public void NonModelRecoveryDoesNotTriggerForMalformedOrOtherBus(
        string value)
    {
        Assert.False(PnpSetupApiIdentity.NeedsPciHardwareModelRecovery([value]));
        var result = PnpSetupApiIdentity.Merge(
            [value], [], [GpuSpecific], [], null, null);
        Assert.False(result.UsedSetupApi);
        Assert.DoesNotContain(GpuSpecific, result.HardwareIds);
    }

    [Fact]
    public void AbsentNativeModelPreservesOriginalIncompleteWmiWithoutFakeMatch()
    {
        const string partial = @"PCI\VEN_10DE";
        var result = PnpSetupApiIdentity.Merge(
            [partial], [@"PCI\CC_0300"], [@"PCI\VEN_10DE", Killer],
            [], null, null);
        Assert.Equal([partial], result.HardwareIds);
        Assert.Equal([@"PCI\CC_0300"], result.CompatibleIds);
        Assert.False(result.UsedSetupApi);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            WithIdentity(result), Wua(GpuSpecific)));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(partial));
    }

}
