using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverSearchPassAuditTests
{
    [Fact]
    public void SuccessRecordsPassAndCandidateCount()
    {
        var status = DriverSearchPassAudit.Run("WU online", () => 7, CancellationToken.None);
        Assert.True(status.Succeeded);
        Assert.Equal(7, status.Candidates);
        Assert.Equal("Completed", status.Outcome);
    }

    [Fact]
    public void ZeroCandidatesIsNotAQueryFailure()
    {
        var status = DriverSearchPassAudit.Run("WU", () => 0, CancellationToken.None);
        Assert.True(status.Succeeded);
        Assert.Equal(0, status.Candidates);
    }

    [Fact]
    public void OfflinePassMustNotPretendToHaveFoundZeroUpdates()
    {
        var logged = false;
        var status = DriverSearchPassAudit.Run(
            "WU advanced",
            () => throw new System.IO.IOException("Private machine detail"),
            CancellationToken.None,
            _ => logged = true);
        Assert.True(logged);
        Assert.False(status.Succeeded);
        Assert.Equal("Failed", status.Outcome);
        Assert.DoesNotContain("Private machine detail", status.Detail);
    }

    [Fact]
    public void ExplicitCancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            DriverSearchPassAudit.Run("WU", () => 10, cts.Token));
    }

    [Fact]
    public void CancellationDuringPassPropagates()
    {
        using var cts = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            DriverSearchPassAudit.Run("WU", () =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }, cts.Token));
    }
}
