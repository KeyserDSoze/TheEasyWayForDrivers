using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// Already-specific PCI VEN+DEV enrichment is strictly optional: it cannot
/// steal the final 256 SetupAPI calls reserved for missing PnP Hardware IDs.
/// </summary>
public sealed class PnpOemSubsystemLookupBudgetTests
{
    private const string Bare = @"PCI\VEN_10DE&DEV_28A0";
    private const string Oem = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";

    [Fact]
    public void OptionalOemLookupsHaveDedicated96AttemptLimit()
    {
        var budget = new PnpNativeRecoveryBudget();
        for (var index = 0; index < 2000; index++)
        {
            Assert.Equal(
                index < PnpNativeRecoveryBudget.MaximumOemSubsystemOnlyLookups,
                budget.TryAcquire(
                    needsHardware: true, needsCompatible: true,
                    oemSubsystemOnly: true));
        }
        Assert.Equal(PnpNativeRecoveryBudget.MaximumOemSubsystemOnlyLookups,
            budget.OemSubsystemOnlyUsed);
        Assert.Equal(PnpNativeRecoveryBudget.MaximumOemSubsystemOnlyLookups,
            budget.Used);
        Assert.Equal(0, budget.CompatibleOnlyUsed);
    }

    [Fact]
    public void AlreadySpentCompatibleBudgetPreservesReservedMissingHardwareReads()
    {
        var budget = new PnpNativeRecoveryBudget();
        for (var i = 0;
            i < PnpNativeRecoveryBudget.MaximumCompatibleOnlyLookups; i++)
            Assert.True(budget.TryAcquire(false, true));
        Assert.Equal(PnpNativeRecoveryBudget.HardwareRecoveryReserve,
            budget.Remaining);
        Assert.False(budget.TryAcquire(
            needsHardware: true, needsCompatible: false,
            oemSubsystemOnly: true));
        Assert.Equal(0, budget.OemSubsystemOnlyUsed);
        Assert.True(budget.TryAcquire(true, false));
        Assert.Equal(PnpNativeRecoveryBudget.HardwareRecoveryReserve - 1,
            budget.Remaining);
    }

    [Fact]
    public void OptionalOemUsesNoReservedSlotsEvenAfterLongPnpInventory()
    {
        var budget = new PnpNativeRecoveryBudget();
        for (var index = 0;
            index < PnpNativeRecoveryBudget.MaximumOemSubsystemOnlyLookups;
            index++)
            Assert.True(budget.TryAcquire(true, false, oemSubsystemOnly: true));
        while (budget.Used <
            PnpNativeRecoveryBudget.MaximumCompatibleOnlyLookups)
            Assert.True(budget.TryAcquire(false, true));

        Assert.False(budget.TryAcquire(true, true, oemSubsystemOnly: true));
        Assert.True(PnpSetupApiIdentity.NeedsPciOemSubsystemRecovery([Bare]));
        Assert.True(budget.TryAcquire(true, true)); // missing-model priority
        var ids = PnpSetupApiIdentity.Merge(
            [Bare], [], [Oem], [], null, null);
        Assert.True(ids.UsedSetupApi);
        Assert.Equal([Bare, Oem], ids.HardwareIds);
    }

    [Fact]
    public void OptionalOemFlagRequiresAnActualHardwareRead()
    {
        var budget = new PnpNativeRecoveryBudget();
        Assert.False(budget.TryAcquire(false, false, oemSubsystemOnly: true));
        Assert.False(budget.TryAcquire(false, true, oemSubsystemOnly: true));
        Assert.Equal(0, budget.Used);

        Assert.True(budget.TryAcquire(true, false, oemSubsystemOnly: true));
        Assert.Equal(1, budget.Used);
        Assert.Equal(1, budget.OemSubsystemOnlyUsed);
    }
}
