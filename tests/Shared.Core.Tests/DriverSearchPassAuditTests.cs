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


    [Theory]
    [InlineData(0x80240025u)]
    [InlineData(0x8024002Eu)]
    [InlineData(0x8024002Fu)]
    public void PolicyErrorsAreNotZeroDriversAndNeverLeakComText(uint code)
    {
        var status = DriverSearchPassAudit.Run("Windows Update online",
            () => throw new System.Runtime.InteropServices.COMException(
                "PRIVATE_USER_PATH", unchecked((int)code)),
            CancellationToken.None);
        Assert.Equal("PolicyRestricted", status.Outcome);
        Assert.False(status.Succeeded);
        Assert.Equal(0, status.Candidates);
        Assert.NotNull(status.AttemptedAtUtc);
        Assert.Contains("criteri", status.Detail);
        Assert.DoesNotContain("PRIVATE_USER_PATH", status.Detail);
    }

    [Theory]
    [InlineData(0x8024001Fu)]
    [InlineData(0x8024401Cu)]
    [InlineData(0x80244022u)]
    [InlineData(0x80244023u)]
    [InlineData(0x8024402Cu)]
    public void NetworkAndServerErrorsDoNotClaimOfflinePc(uint code)
    {
        var status = DriverSearchPassAudit.Run("Windows Update online",
            () => throw new System.Runtime.InteropServices.COMException(
                "PRIVATE_PROXY_HOST", unchecked((int)code)),
            CancellationToken.None);
        Assert.Equal("NetworkError", status.Outcome);
        Assert.Contains("non prova PC offline", status.Detail);
        Assert.DoesNotContain("PRIVATE_PROXY_HOST", status.Detail);
    }

    [Fact]
    public void UnknownComAndIOExceptionRemainFailed()
    {
        var com = DriverSearchPassAudit.Run("Windows Update",
            () => throw new System.Runtime.InteropServices.COMException(
                "PRIVATE", unchecked((int)0x80240032u)),
            CancellationToken.None);
        var io = DriverSearchPassAudit.Run("Windows Update",
            () => throw new IOException("PRIVATE FILE"),
            CancellationToken.None);
        Assert.Equal("Failed", com.Outcome);
        Assert.Equal("Failed", io.Outcome);
        Assert.NotNull(io.AttemptedAtUtc);
        Assert.DoesNotContain("PRIVATE", io.Detail);
    }

    [Fact]
    public void SuccessfulPassIsStampedWithUtcStart()
    {
        var before = DateTimeOffset.UtcNow;
        var status = DriverSearchPassAudit.Run(
            "Windows Update", () => 0, CancellationToken.None);
        var after = DateTimeOffset.UtcNow;
        Assert.NotNull(status.AttemptedAtUtc);
        Assert.InRange(status.AttemptedAtUtc.Value, before, after);
        Assert.Equal(TimeSpan.Zero, status.AttemptedAtUtc.Value.Offset);
        Assert.True(status.Succeeded);
        Assert.Equal(0, status.Candidates);
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
