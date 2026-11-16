using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #513: a compatible node returned by SetupDiEnumDriverInfo still counts
/// even when SetupDiGetDriverInstallParams cannot read its rank. Neither
/// local rank nor its absence proves an installable/new driver update.
/// </summary>
public sealed class WindowsSetupApiUnreadableRankAccountingTests
{
    private static WindowsSetupApiRankEvidence.Driver Driver(
        uint rank, string name) =>
        new(rank, name, "NVIDIA",
            0x00200000000F0001UL,
            new DateTimeOffset(2026, 11, 1, 0, 0, 0,
                TimeSpan.Zero));

    [Fact]
    public void MixedReadableAndUnreadableNodesCountAsEnumeratedButNeverRankZero()
    {
        var report = WindowsSetupApiRankEvidence.Summarize(
            [Driver(0x00FF0001, "Better rank"),
             Driver(0x00FF2001, "Worse rank")],
            complete: true, driversWithoutRank: 3);

        Assert.Equal(WindowsSetupApiRankEvidence.State.Available, report.Status);
        Assert.Equal(5, report.DriversEnumerated);
        Assert.Equal(2, report.DriversShown);
        Assert.Equal(3, report.DriversWithoutRank);
        Assert.True(report.Incomplete);
        Assert.Contains("Nodi driver locali enumerati: 5", report.DisplayText);
        Assert.Contains("rank leggibili: 2", report.DisplayText);
        Assert.Contains("rank non letti: 3", report.DisplayText);
        Assert.Contains("3 nodi driver compatibili", report.DisplayText);
        Assert.Contains("NON hanno rank zero", report.DisplayText);
        Assert.Contains("Verifica PARZIALE", report.DisplayText);
        Assert.True(report.DisplayText.IndexOf("Better rank",
            StringComparison.Ordinal) <
            report.DisplayText.IndexOf("Worse rank",
                StringComparison.Ordinal));
        Assert.DoesNotContain("Rank 0x00000000", report.DisplayText);
        Assert.False(report.IsNewDriverUpdate);
        Assert.False(report.IsInstallable);
        Assert.False(report.HasVerifiedExternalOemRanking);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(256)]
    public void AllRanksUnreadablePreserveNodeCountWithoutInventingDrivers(
        int unreadable)
    {
        var report = WindowsSetupApiRankEvidence.Summarize(
            [], complete: true, driversWithoutRank: unreadable);
        Assert.Equal(WindowsSetupApiRankEvidence.State.NotVerified,
            report.Status);
        Assert.Equal(unreadable, report.DriversEnumerated);
        Assert.Equal(unreadable, report.DriversWithoutRank);
        Assert.Equal(0, report.DriversShown);
        Assert.True(report.Incomplete);
        Assert.Contains($"{unreadable} nodi driver compatibili locali",
            report.DisplayText);
        Assert.Contains("nessun rank è stato letto", report.DisplayText);
        Assert.Contains("non significa zero driver", report.DisplayText);
        Assert.DoesNotContain("Rank 0x", report.DisplayText);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsNewDriverUpdate);
    }

    [Fact]
    public void FullyReadableListKeepsExactTotalsAndNotPartial()
    {
        var report = WindowsSetupApiRankEvidence.Summarize(
            [Driver(1, "Realtek Audio"), Driver(2, "Intel Killer")],
            complete: true);
        Assert.Equal(2, report.DriversEnumerated);
        Assert.Equal(2, report.DriversShown);
        Assert.Equal(0, report.DriversWithoutRank);
        Assert.False(report.Incomplete);
        Assert.DoesNotContain("Verifica PARZIALE", report.DisplayText);
        Assert.Contains("rank non letti: 0", report.DisplayText);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void NativeEnumerationFailureStillMakesPartialWhenRankReadSucceeded()
    {
        var report = WindowsSetupApiRankEvidence.Summarize(
            [Driver(1, "NVIDIA")], complete: false);
        Assert.Equal(1, report.DriversEnumerated);
        Assert.Equal(0, report.DriversWithoutRank);
        Assert.True(report.Incomplete);
        Assert.Contains("Verifica PARZIALE", report.DisplayText);
    }

    [Fact]
    public void EmptyListRemainsNotVerifiedNotClaimedZeroLocalDrivers()
    {
        var empty = WindowsSetupApiRankEvidence.Summarize(
            [], complete: false, driversWithoutRank: 0);
        Assert.Equal(WindowsSetupApiRankEvidence.NotVerified(), empty);
        Assert.True(empty.Incomplete);
        Assert.False(empty.IsInstallable);
    }

    [Fact]
    public void AggregateBudgetCountsUnreadableNativeNodes()
    {
        var maximum = WindowsSetupApiRankEvidence.MaxEnumeratedDrivers;
        var accepted = WindowsSetupApiRankEvidence.Summarize(
            [Driver(1, "NVIDIA")], complete: false,
            driversWithoutRank: maximum - 1);
        Assert.Equal(maximum, accepted.DriversEnumerated);
        Assert.True(accepted.Incomplete);

        Assert.Throws<InvalidDataException>(() =>
            WindowsSetupApiRankEvidence.Summarize(
                [Driver(1, "NVIDIA")], true,
                driversWithoutRank: maximum));
        Assert.Throws<InvalidDataException>(() =>
            WindowsSetupApiRankEvidence.Summarize(
                [], true, driversWithoutRank: maximum + 1));
        Assert.Throws<InvalidDataException>(() =>
            WindowsSetupApiRankEvidence.Summarize(
                [Driver(1, "NVIDIA")], true,
                driversWithoutRank: -1));
        Assert.Throws<ArgumentNullException>(() =>
            WindowsSetupApiRankEvidence.Summarize(
                null!, true, driversWithoutRank: 1));
    }

    [Fact]
    public void UnreadableRankCountsDoNotRevealPrivatePnpData()
    {
        var report = WindowsSetupApiRankEvidence.Summarize(
            [new WindowsSetupApiRankEvidence.Driver(
                0x00FF0001,
                @"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL",
                @"C:\Users\PRIVATE_PERSON\driver.inf",
                0)],
            complete: false, driversWithoutRank: 3);

        Assert.Contains("[etichetta oscurata]", report.DisplayText);
        Assert.DoesNotContain("PRIVATE_", report.DisplayText);
        Assert.DoesNotContain("SERIAL", report.DisplayText);
        Assert.DoesNotContain(@"PCI\", report.DisplayText);
        Assert.DoesNotContain(@"C:\Users", report.DisplayText);
        Assert.Equal(4, report.DriversEnumerated);
        Assert.Equal(3, report.DriversWithoutRank);
        Assert.False(report.IsInstallable);
    }
}
