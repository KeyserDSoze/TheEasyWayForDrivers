using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

public sealed class WuaBatchIntegrityTests
{
    public sealed class FakeUpdateResult(int resultCode, bool rebootRequired = false)
    {
        public int ResultCode { get; } = resultCode;
        public bool RebootRequired { get; } = rebootRequired;
    }

    public sealed class FakeBatchResult(
        int resultCode, params FakeUpdateResult[] results)
    {
        public int ResultCode { get; } = resultCode;
        public bool RebootRequired { get; init; }
        public Action<int>? BeforeGet { get; init; }
        public int ReadCount { get; private set; }

        public FakeUpdateResult GetUpdateResult(int index)
        {
            ReadCount++;
            BeforeGet?.Invoke(index);
            return results[index];
        }
    }

    public sealed class MissingPerItemResult
    {
        public int ResultCode => 2;
        public bool RebootRequired => false;
    }

    public sealed class MissingGlobalResult
    {
        public bool RebootRequired => false;
    }

    [Fact]
    public void FullDownloadMayProceedOnlyWhenEveryUpdateSucceeds()
    {
        var batch = new FakeBatchResult(2,
            new FakeUpdateResult(2), new FakeUpdateResult(2));
        var outcome = WuaBatchResultVerifier.Read(batch, 2, isInstallation: false);

        Assert.True(outcome.FullySucceeded);
        Assert.Equal(2, outcome.ConfirmedSucceeded);
        Assert.Equal(0, outcome.NotConfirmed);
        Assert.Equal(2, batch.ReadCount);
        Assert.False(outcome.RebootRequired);
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(45, 2)]
    public void NonSuccessfulGlobalDownloadNeverAuthorizesInstall(
        int global, int item)
    {
        var batch = new FakeBatchResult(global, new FakeUpdateResult(item));
        var outcome = WuaBatchResultVerifier.Read(batch, 1, isInstallation: false);

        Assert.False(outcome.FullySucceeded);
        Assert.Contains("Nessuna installazione avviata", outcome.DownloadMessage);
        Assert.DoesNotContain("PCI\\VEN", outcome.DownloadMessage);
        if (global is not (2 or 3))
            Assert.Equal(0, batch.ReadCount);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(1)]
    public void PerUpdateDownloadErrorRejectsGloballySuccessfulBatch(int errorCode)
    {
        var batch = new FakeBatchResult(2,
            new FakeUpdateResult(2), new FakeUpdateResult(errorCode));
        var outcome = WuaBatchResultVerifier.Read(batch, 2, false);

        Assert.False(outcome.FullySucceeded);
        Assert.Equal(1, outcome.ConfirmedSucceeded);
        Assert.Equal(1, outcome.NotConfirmed);
        Assert.Contains("1 riusciti, 1 non confermati", outcome.DownloadMessage);
    }

    [Fact]
    public void DownloadMissingPerItemEvidenceFailsClosed()
    {
        var outcome = WuaBatchResultVerifier.Read(
            new MissingPerItemResult(), 2, false);

        Assert.False(outcome.FullySucceeded);
        Assert.Equal(0, outcome.ConfirmedSucceeded);
        Assert.Equal(2, outcome.NotConfirmed);
    }

    [Fact]
    public void DownloadWithFewerItemResultsIsNotSuccessful()
    {
        var batch = new FakeBatchResult(2, new FakeUpdateResult(2));
        var outcome = WuaBatchResultVerifier.Read(batch, 2, false);

        Assert.False(outcome.FullySucceeded);
        Assert.Equal(1, outcome.ConfirmedSucceeded);
        Assert.Equal(1, outcome.NotConfirmed);
    }

    [Fact]
    public void SuccessfulInstallRequiresGlobalAndEveryPerUpdateResult()
    {
        var batch = new FakeBatchResult(2,
            new FakeUpdateResult(2), new FakeUpdateResult(2))
        {
            RebootRequired = false
        };

        var outcome = WuaBatchResultVerifier.Read(batch, 2, true);
        Assert.True(outcome.FullySucceeded);
        Assert.Equal(2, outcome.ConfirmedSucceeded);
        Assert.False(outcome.RebootRequired);
    }

    [Fact]
    public void PartialInstallationPreservesSuccessfulCountAndRequiredReboot()
    {
        var batch = new FakeBatchResult(3,
            new FakeUpdateResult(2), new FakeUpdateResult(4, rebootRequired: true));
        var outcome = WuaBatchResultVerifier.Read(batch, 2, true);

        Assert.False(outcome.FullySucceeded);
        Assert.Equal(1, outcome.ConfirmedSucceeded);
        Assert.Equal(1, outcome.NotConfirmed);
        Assert.True(outcome.RebootRequired);
        Assert.Contains("parziale o non verificabile", outcome.InstallMessage);
        Assert.Contains("alcuni driver potrebbero", outcome.InstallMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE_SERIAL", outcome.InstallMessage);
    }

    [Fact]
    public void AggregatePartialInstallationIsNotSuccessEvenIfAllItemsSucceed()
    {
        var batch = new FakeBatchResult(3,
            new FakeUpdateResult(2), new FakeUpdateResult(2));
        var outcome = WuaBatchResultVerifier.Read(batch, 2, true);

        Assert.False(outcome.FullySucceeded);
        Assert.Equal(2, outcome.ConfirmedSucceeded);
        Assert.Equal(0, outcome.NotConfirmed);
    }

    [Fact]
    public void GloballySuccessfulInstallWithFailedUpdateMustNotReportSuccess()
    {
        var batch = new FakeBatchResult(2,
            new FakeUpdateResult(2), new FakeUpdateResult(4))
        {
            RebootRequired = true
        };
        var outcome = WuaBatchResultVerifier.Read(batch, 2, true);

        Assert.False(outcome.FullySucceeded);
        Assert.Equal(1, outcome.ConfirmedSucceeded);
        Assert.True(outcome.RebootRequired);
    }

    [Fact]
    public void MissingInstallComDetailsAreNotInterpretedAsSuccess()
    {
        var outcome = WuaBatchResultVerifier.Read(
            new MissingGlobalResult(), 1, true);

        Assert.False(outcome.FullySucceeded);
        Assert.Equal(1, outcome.NotConfirmed);
        Assert.Contains("ripetere la scansione", outcome.InstallMessage);
    }

    [Fact]
    public void InstallationCanRequireRebootFromSingleUpdate()
    {
        var batch = new FakeBatchResult(2, new FakeUpdateResult(2, true));
        var outcome = WuaBatchResultVerifier.Read(batch, 1, true);

        Assert.True(outcome.FullySucceeded);
        Assert.True(outcome.RebootRequired);
    }

    [Fact]
    public void MissingResultIsNotSuccess()
    {
        var outcome = WuaBatchResultVerifier.Read(null, 2, false);

        Assert.False(outcome.FullySucceeded);
        Assert.Equal(2, outcome.NotConfirmed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(257)]
    public void RejectsZeroOrOverBudgetBatches(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WuaBatchResultVerifier.Read(
                new FakeBatchResult(2, new FakeUpdateResult(2)), count, false));

    [Fact]
    public void CancellationPropagatesEvenDuringResultEnumeration()
    {
        using var cancellation = new CancellationTokenSource();
        var batch = new FakeBatchResult(2,
            new FakeUpdateResult(2), new FakeUpdateResult(2))
        {
            BeforeGet = index =>
            {
                if (index == 0)
                    cancellation.Cancel();
            }
        };

        Assert.ThrowsAny<OperationCanceledException>(() =>
            WuaBatchResultVerifier.Read(batch, 2, false, cancellation.Token));
    }

    [Fact]
    public void SelectionGateRequiresAllGuidIdsRecheckedWithoutSubstitutions()
    {
        var first = Guid.NewGuid().ToString("D");
        var second = Guid.NewGuid().ToString("D");
        IReadOnlySet<string> selected =
            new HashSet<string>([first, second], StringComparer.OrdinalIgnoreCase);

        Assert.True(WuaBatchSelectionGate.IsValidRequest(selected));
        Assert.False(WuaBatchSelectionGate.IsComplete(
            selected, new HashSet<string>([first],
                StringComparer.OrdinalIgnoreCase), 1));
        Assert.False(WuaBatchSelectionGate.IsComplete(
            selected, new HashSet<string>([first],
                StringComparer.OrdinalIgnoreCase), 2));
        Assert.True(WuaBatchSelectionGate.IsComplete(
            selected, new HashSet<string>([first.ToUpperInvariant(), second],
                StringComparer.OrdinalIgnoreCase), 2));
    }

    [Fact]
    public void SelectionGateDoesNotAcceptForeignIdsOrFakeUpdateIdentifiers()
    {
        var expectedId = Guid.NewGuid().ToString("D");
        IReadOnlySet<string> requested = new HashSet<string>([expectedId],
            StringComparer.OrdinalIgnoreCase);
        IReadOnlySet<string> foreign = new HashSet<string>(
            [Guid.NewGuid().ToString("D")], StringComparer.OrdinalIgnoreCase);

        Assert.False(WuaBatchSelectionGate.IsComplete(requested, foreign, 1));
        Assert.False(WuaBatchSelectionGate.IsValidRequest(
            new HashSet<string>(["not-a-guid"])));
        Assert.False(WuaBatchSelectionGate.IsValidRequest(
            new HashSet<string>()));
        Assert.False(WuaBatchSelectionGate.IsValidRequest(
            new HashSet<string>(Enumerable.Range(0, 257)
                .Select(_ => Guid.NewGuid().ToString("D")))));
    }
}
