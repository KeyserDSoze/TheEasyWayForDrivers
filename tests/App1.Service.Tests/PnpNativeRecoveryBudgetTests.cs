using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// Simulation of long Win32_PnPEntity enumerations. Optional compatible-only
/// reads may not consume the final native lookup budget needed by late
/// NVIDIA, Killer and audio instances with missing/generic Hardware IDs.
/// No SetupAPI, driver download or hardware mutation runs in these tests.
/// </summary>
public sealed class PnpNativeRecoveryBudgetTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string SoundWire = @"SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711";
    private const string GuidOne = "00000000-0000-4000-8000-000000000359";

    private static DriverInfo Device(string hardware, string deviceId) =>
        new(deviceId, "Microsoft Generic Device", "Microsoft",
            "System", "Microsoft", "1.0", null, "PRIVATE_OEM.inf",
            true, true, 0, [hardware], []);

    [Fact]
    public void CompatibleOnlyFloodCannotConsumeHardwareReserve()
    {
        var budget = new PnpNativeRecoveryBudget();
        for (var index = 0; index < 10000; index++)
        {
            var accepted = budget.TryAcquire(
                needsHardware: false, needsCompatible: true);
            Assert.Equal(
                index < PnpNativeRecoveryBudget.MaximumCompatibleOnlyLookups,
                accepted);
        }

        Assert.Equal(PnpNativeRecoveryBudget.MaximumCompatibleOnlyLookups,
            budget.Used);
        Assert.Equal(PnpNativeRecoveryBudget.HardwareRecoveryReserve,
            budget.Remaining);

        // WMI now encounters *late* devices after thousands of other
        // PnP rows; the native same-instance model lookup can still run.
        for (var index = 0;
            index < PnpNativeRecoveryBudget.HardwareRecoveryReserve; index++)
        {
            Assert.True(budget.TryAcquire(
                needsHardware: true, needsCompatible: index % 2 == 0));
        }

        Assert.Equal(PnpNativeRecoveryBudget.MaximumLookups, budget.Used);
        Assert.Equal(0, budget.Remaining);
        Assert.False(budget.TryAcquire(true, false));
        Assert.False(budget.TryAcquire(false, true));
    }

    [Fact]
    public void NoLookupNeededOrInvalidRequestCannotSpendBudget()
    {
        var budget = new PnpNativeRecoveryBudget();

        for (var index = 0; index < 10000; index++)
            Assert.False(budget.TryAcquire(false, false));

        Assert.Equal(0, budget.Used);
        Assert.Equal(PnpNativeRecoveryBudget.MaximumLookups,
            budget.Remaining);
        Assert.True(budget.TryAcquire(false, true));
        Assert.True(budget.TryAcquire(true, false));
        Assert.True(budget.TryAcquire(true, true));
        Assert.Equal(3, budget.Used);
        Assert.Equal(1, budget.CompatibleOnlyUsed);
    }

    [Fact]
    public void HardwareRequestsShareSameAbsolute2048Limit()
    {
        var budget = new PnpNativeRecoveryBudget();
        for (var index = 0; index <
            PnpNativeRecoveryBudget.MaximumLookups; index++)
            Assert.True(budget.TryAcquire(true, index % 2 == 0));

        Assert.Equal(PnpNativeRecoveryBudget.MaximumLookups, budget.Used);
        Assert.Equal(0, budget.CompatibleOnlyUsed);
        Assert.False(budget.TryAcquire(true, true));
        Assert.False(budget.TryAcquire(true, false));
        Assert.False(budget.TryAcquire(false, true));
    }

    [Fact]
    public void MixedRequestsDoNotStealTheFinal256HardwareSlots()
    {
        var budget = new PnpNativeRecoveryBudget();
        for (var index = 0; index < 1000; index++)
            Assert.True(budget.TryAcquire(true, false));
        for (var index = 0; index < 792; index++)
            Assert.True(budget.TryAcquire(false, true));

        Assert.Equal(1792, budget.Used);
        Assert.Equal(792, budget.CompatibleOnlyUsed);
        Assert.Equal(256, budget.Remaining);
        Assert.False(budget.TryAcquire(false, true));
        Assert.True(budget.TryAcquire(true, true));
        Assert.Equal(255, budget.Remaining);
        Assert.False(budget.TryAcquire(false, true));
    }

    [Fact]
    public void NormalInventoriesKeepNativeCompatibleOnlyRecovery()
    {
        var budget = new PnpNativeRecoveryBudget();
        for (var index = 0; index < 550; index++)
            Assert.True(budget.TryAcquire(false, true));

        Assert.True(budget.TryAcquire(true, false));
        Assert.True(budget.TryAcquire(true, true));
        Assert.Equal(552, budget.Used);
        Assert.Equal(550, budget.CompatibleOnlyUsed);
        Assert.Equal(1496, budget.Remaining);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE", Nvidia)]
    [InlineData(@"PCI\VEN_8086", Killer)]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC", Realtek)]
    [InlineData(@"SOUNDWIRE\AGGREGATOR", SoundWire)]
    public void ReservedSameInstanceReadCanRecoverExactModelForWuaAndInf(
        string wmiGeneric, string nativeSpecific)
    {
        var budget = new PnpNativeRecoveryBudget();
        // Simulate early rows with known hardware but absent CompatibleIDs.
        for (var index = 0; index < 2200; index++)
            budget.TryAcquire(false, true);

        Assert.Equal(1792, budget.Used);
        Assert.True(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [wmiGeneric]));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(wmiGeneric));
        Assert.True(budget.TryAcquire(true, true));

        // PnpSetupApiIdentity.Merge performs the SAME-INSTANCE strict
        // model/vendor/bus check. It never borrows an unrelated ID or
        // promotes generic WMI to a driver candidate by itself.
        var ids = PnpSetupApiIdentity.Merge(
            [wmiGeneric], [], [nativeSpecific], [], null, null);
        Assert.True(ids.UsedSetupApi);
        Assert.Equal(nativeSpecific, ids.HardwareIds[0]);
        Assert.Contains(wmiGeneric, ids.HardwareIds);

        var device = Device(nativeSpecific, "PRIVATE_DEVICE_INSTANCE");
        var update = new DriverUpdateInfo(
            GuidOne, "Windows driver", null, "System", "Microsoft",
            null, null, false, "Microsoft", null, nativeSpecific,
            null, SearchSource: "Windows Update online");
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(device, update));

        var foreign = PnpSetupApiIdentity.Merge(
            [wmiGeneric], [], [@"PCI\VEN_1002&DEV_73DF"], [], null, null);
        Assert.False(foreign.UsedSetupApi);
    }

    [Fact]
    public void AVeryLateNvidiaGpuStillGetsHardwareReadAfter2048CompatibleGaps()
    {
        var budget = new PnpNativeRecoveryBudget();
        for (var index = 0; index < 2048; index++)
            budget.TryAcquire(false, true);

        Assert.True(budget.TryAcquire(true, false));
        var recovered = PnpSetupApiIdentity.Merge(
            [@"PCI\VEN_10DE"], [], [Nvidia], [], null, null);
        Assert.Equal(Nvidia, recovered.HardwareIds[0]);
        Assert.True(recovered.UsedSetupApi);
        Assert.Equal(1793, budget.Used);
    }
}
