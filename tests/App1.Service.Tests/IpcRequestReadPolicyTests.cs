using System.IO;
using TheEasyWayForDrivers.ServiceApp.Ipc;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// #432: a stalled connected client must not monopolize the server pipe.
/// No Windows or driver operations are invoked by these tests.
/// </summary>
public sealed class IpcRequestReadPolicyTests
{
    [Fact]
    public async Task NormalDesktopJsonIsUnaffected()
    {
        using var reader = new StringReader(
            "{\"command\":\"search-report\",\"updateIds\":null}\r\n");
        var result = await IpcRequestReadPolicy.ReadAsync(
            reader, 64 * 1024, TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.False(result.TooLarge);
        Assert.Equal(
            "{\"command\":\"search-report\",\"updateIds\":null}",
            result.Line);
    }

    [Fact]
    public async Task OversizedRequestStillHasItsDistinctOutcome()
    {
        using var reader = new StringReader(new string('X', 129) + "\n");
        var result = await IpcRequestReadPolicy.ReadAsync(
            reader, 128, TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.True(result.TooLarge);
        Assert.Null(result.Line);
    }

    [Fact]
    public async Task SilentConnectedClientExpires()
    {
        using var reader = new BlockingReader();
        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await IpcRequestReadPolicy.ReadAsync(
                reader, 64 * 1024, TimeSpan.FromMilliseconds(100),
                CancellationToken.None));
        Assert.Equal("IPC request read timed out.", exception.Message);
        Assert.Equal(1, reader.Attempts);
    }

    [Fact]
    public async Task PartialJsonWithoutNewlineExpires()
    {
        using var reader = new PrefixThenBlockingReader("{\"command\":\"scan\"");
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await IpcRequestReadPolicy.ReadAsync(
                reader, 64 * 1024, TimeSpan.FromMilliseconds(100),
                CancellationToken.None));
        Assert.True(reader.Attempts > 1);
    }

    [Fact]
    public async Task ServiceShutdownIsNotReclassifiedAsTimeout()
    {
        using var stop = new CancellationTokenSource();
        using var reader = new BlockingReader();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await IpcRequestReadPolicy.ReadAsync(
                reader, 64 * 1024, TimeSpan.FromSeconds(1),
                stop.Token));
        Assert.Equal(0, reader.Attempts);
    }

    [Fact]
    public async Task ServiceShutdownDuringReadRemainsCancellation()
    {
        using var stop = new CancellationTokenSource();
        using var reader = new BlockingReader();
        stop.CancelAfter(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await IpcRequestReadPolicy.ReadAsync(
                reader, 64 * 1024, TimeSpan.FromSeconds(5),
                stop.Token));
    }

    [Fact]
    public async Task ZeroTimeoutIsRejected()
    {
        using var reader = new StringReader("hello");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await IpcRequestReadPolicy.ReadAsync(
                reader, 64, TimeSpan.Zero, CancellationToken.None));
    }

    private class BlockingReader : TextReader
    {
        public int Attempts { get; protected set; }

        public override async ValueTask<int> ReadAsync(
            Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            Attempts++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class PrefixThenBlockingReader(string prefix) : BlockingReader
    {
        private int position;

        public override ValueTask<int> ReadAsync(
            Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (position >= prefix.Length)
                return base.ReadAsync(buffer, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            prefix.AsMemory(position, 1).CopyTo(buffer);
            position++;
            Attempts++;
            return ValueTask.FromResult(1);
        }
    }
}
