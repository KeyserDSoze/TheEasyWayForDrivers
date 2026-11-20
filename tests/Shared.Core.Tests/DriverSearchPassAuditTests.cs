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

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NegativeQueryCountIsFailureNotVerifiedZero(int badCount)
    {
        var status = DriverSearchPassAudit.Run(
            "Windows Update online", () => badCount, CancellationToken.None);
        Assert.Equal("Failed", status.Outcome);
        Assert.False(status.Succeeded);
        Assert.Equal(0, status.Candidates);
        Assert.NotNull(status.AttemptedAtUtc);
        Assert.DoesNotContain(badCount.ToString(), status.Detail);
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

    [Fact]
    public void OutOfMemoryFromComPassPropagatesWithoutReportingOrdinaryFailure()
    {
        // A critical resource failure cannot be presented as a normal
        // failed/zero-result WUA pass that the scanner can continue past.
        var failureCallbacks = 0;
        var exception = Assert.Throws<OutOfMemoryException>(() =>
            DriverSearchPassAudit.Run(
                "Windows Update online",
                () => throw new OutOfMemoryException("PRIVATE_DEVICE_MEMORY"),
                CancellationToken.None,
                _ => failureCallbacks++));
        Assert.Equal("PRIVATE_DEVICE_MEMORY", exception.Message);
        Assert.Equal(0, failureCallbacks);
    }

    [Fact]
    public void OutOfMemoryIsNotSwallowedEvenWhenCancellationWasRequested()
    {
        using var cancelled = new CancellationTokenSource();
        var failureCallbacks = 0;
        Assert.Throws<OutOfMemoryException>(() =>
            DriverSearchPassAudit.Run(
                "Windows configurato",
                () =>
                {
                    cancelled.Cancel();
                    throw new OutOfMemoryException("PRIVATE_OOM");
                },
                cancelled.Token,
                _ => failureCallbacks++));
        Assert.Equal(0, failureCallbacks);
    }

    [Fact]
    public void RequestedCancellationMidQueryNeverTriggersFailureCallback()
    {
        using var cancelled = new CancellationTokenSource();
        var failureCallbacks = 0;
        Assert.ThrowsAny<OperationCanceledException>(() =>
            DriverSearchPassAudit.Run(
                "Windows Update online",
                () =>
                {
                    cancelled.Cancel();
                    throw new OperationCanceledException(
                        "PRIVATE_CANCEL", cancelled.Token);
                },
                cancelled.Token,
                _ => failureCallbacks++));
        Assert.Equal(0, failureCallbacks);
    }

    [Fact]
    public void ComAndIoFailuresRemainNonVerifiedWithPrivacySafeDetails()
    {
        var failureCallbacks = 0;
        var network = DriverSearchPassAudit.Run(
            "Windows Update online",
            () => throw new System.Runtime.InteropServices.COMException(
                "PRIVATE_WSUS_URL", unchecked((int)0x80244022u)),
            CancellationToken.None,
            _ => failureCallbacks++);
        var input = DriverSearchPassAudit.Run(
            "Windows configurato",
            () => throw new IOException("PRIVATE_HARDWARE_ID"),
            CancellationToken.None,
            _ => failureCallbacks++);

        Assert.Equal(2, failureCallbacks);
        Assert.Equal("NetworkError", network.Outcome);
        Assert.Equal("Failed", input.Outcome);
        Assert.False(network.Succeeded);
        Assert.False(input.Succeeded);
        Assert.Equal(0, network.Candidates);
        Assert.Equal(0, input.Candidates);
        Assert.Equal(0, network.NewUniqueCandidates);
        Assert.Equal(0, input.NewUniqueCandidates);
        Assert.DoesNotContain("PRIVATE", network.Detail + input.Detail);
    }

    [Fact]
    public void NonRequestedProviderCancellationIsStillUnverified()
    {
        var failureCallbacks = 0;
        var result = DriverSearchPassAudit.Run(
            "Windows Update online",
            () => throw new OperationCanceledException("PRIVATE_PROVIDER_CANCEL"),
            CancellationToken.None,
            _ => failureCallbacks++);

        Assert.Equal("Failed", result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal(1, failureCallbacks);
        Assert.DoesNotContain("PRIVATE", result.Detail);
    }
    [Theory]
    [InlineData("COM")]
    [InlineData("IO")]
    [InlineData("Partial")]
    public void UserCancellationBeforeRecoverableErrorNeverPublishesWuaFailure(
        string errorKind)
    {
        using var cancellation = new CancellationTokenSource();
        var failureCallbacks = 0;

        Assert.ThrowsAny<OperationCanceledException>(() =>
            DriverSearchPassAudit.Run(
                "Windows Update online",
                () =>
                {
                    // WUA/COM can finish with an error at the exact moment
                    // the user cancels: cancellation is authoritative.
                    cancellation.Cancel();
                    return errorKind switch
                    {
                        "COM" => throw new System.Runtime.InteropServices.COMException(
                            "PRIVATE_WSUS_URL", unchecked((int)0x80244022u)),
                        "IO" => throw new IOException("PRIVATE_HARDWARE_ID"),
                        "Partial" => throw new WuaPartialSearchException(),
                        _ => throw new ArgumentOutOfRangeException(nameof(errorKind))
                    };
                },
                cancellation.Token,
                _ => failureCallbacks++));
        Assert.Equal(0, failureCallbacks);
    }

    [Theory]
    [InlineData("COM")]
    [InlineData("Partial")]
    public void CancellationRequestedByFailureObserverCannotEmitACompletedPass(
        string errorKind)
    {
        using var cancellation = new CancellationTokenSource();
        var failureCallbacks = 0;
        Assert.ThrowsAny<OperationCanceledException>(() =>
            DriverSearchPassAudit.Run(
                "Windows Update configurato",
                () => errorKind == "COM"
                    ? throw new System.Runtime.InteropServices.COMException(
                        "PRIVATE_POLICY", unchecked((int)0x80240025u))
                    : throw new WuaPartialSearchException(),
                cancellation.Token,
                _ =>
                {
                    failureCallbacks++;
                    cancellation.Cancel();
                }));
        Assert.Equal(1, failureCallbacks);
    }

    [Fact]
    public void PartialWindowsSearchWithoutCancellationStillReportsUnverified()
    {
        var failures = 0;
        var result = DriverSearchPassAudit.Run(
            "Windows Update avanzato",
            () => throw new WuaPartialSearchException(),
            CancellationToken.None,
            _ => failures++);
        Assert.Equal("Partial", result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Candidates);
        Assert.Equal(1, failures);
        Assert.DoesNotContain("PRIVATE_", result.Detail);
        Assert.NotNull(result.AttemptedAtUtc);
    }

}
