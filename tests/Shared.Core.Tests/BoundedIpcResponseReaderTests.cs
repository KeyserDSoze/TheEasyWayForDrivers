using System.IO;
using TheEasyWayForDrivers.Core.Ipc;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Regression for #434: Desktop IPC frames must be bounded BEFORE JSON parse
/// while preserving all progress/result frames in the same read buffer.
/// </summary>
public sealed class BoundedIpcResponseReaderTests
{
    [Fact]
    public async Task ConsecutiveProgressAndResultFramesSurviveSingleRead()
    {
        using var input = new StringReader(
            "{\"kind\":\"progress\"}\r\n" +
            "{\"kind\":\"progress\"}\n" +
            "{\"kind\":\"result\"}\r\n");
        var reader = new BoundedIpcResponseReader(input, 64);

        Assert.Equal("{\"kind\":\"progress\"}",
            await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("{\"kind\":\"progress\"}",
            await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("{\"kind\":\"result\"}",
            await reader.ReadLineAsync(CancellationToken.None));
        Assert.Null(await reader.ReadLineAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("ABCD\n")]
    [InlineData("ABCD\r")]
    [InlineData("ABCD\r\n")]
    [InlineData("ABCD")]
    public async Task ExactlyMaximumCharactersAreAccepted(string source)
    {
        using var input = new StringReader(source);
        var reader = new BoundedIpcResponseReader(input, 4);

        Assert.Equal("ABCD", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Null(await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CarriageReturnBoundaryDoesNotSwallowNextFrame()
    {
        using var input = new ChunkedReader("first\r\nsecond\rthird\nfourth", 6);
        var reader = new BoundedIpcResponseReader(input, 32);

        foreach (var expected in new[] { "first", "second", "third", "fourth" })
            Assert.Equal(expected, await reader.ReadLineAsync(CancellationToken.None));
        Assert.Null(await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UnterminatedGiganticFrameStopsBeforeAllocatingAllOfIt()
    {
        using var input = new CountingReader(new string('X', 1024 * 1024));
        var reader = new BoundedIpcResponseReader(input, 64);

        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await reader.ReadLineAsync(CancellationToken.None));

        Assert.Equal("IPC response exceeds the maximum permitted size.", error.Message);
        Assert.InRange(input.CharactersRead, 65, 4096);
        Assert.DoesNotContain("XXXX", error.Message);
    }

    [Fact]
    public async Task OversizedSecondFrameDoesNotInvalidateFirstFrame()
    {
        using var input = new StringReader("ok\n" + new string('x', 33) + "\n");
        var reader = new BoundedIpcResponseReader(input, 32);

        Assert.Equal("ok", await reader.ReadLineAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task BlankLineAndEmptyStreamHaveDifferentResults()
    {
        using var input = new StringReader("\n");
        var reader = new BoundedIpcResponseReader(input, 8);
        Assert.Equal("", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Null(await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CancellationBeforeReadDoesNotConsumeStream()
    {
        using var token = new CancellationTokenSource();
        using var input = new CountingReader("sensitive response");
        token.Cancel();
        var reader = new BoundedIpcResponseReader(input, 64);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await reader.ReadLineAsync(token.Token));
        Assert.Equal(0, input.CharactersRead);
    }

    [Fact]
    public async Task CancellationWhileWaitingOnPipeIsPreserved()
    {
        using var token = new CancellationTokenSource();
        token.CancelAfter(TimeSpan.FromMilliseconds(100));
        using var input = new StalledReader();
        var reader = new BoundedIpcResponseReader(input, 64);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await reader.ReadLineAsync(token.Token));
    }

    [Fact]
    public void InvalidLimitIsRejected()
    {
        using var input = new StringReader("x");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundedIpcResponseReader(input, 0));
    }

    private sealed class CountingReader(string data) : TextReader
    {
        private int position;
        public int CharactersRead => position;

        public override ValueTask<int> ReadAsync(
            Memory<char> destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position >= data.Length)
                return ValueTask.FromResult(0);
            var size = Math.Min(destination.Length, data.Length - position);
            data.AsMemory(position, size).CopyTo(destination);
            position += size;
            return ValueTask.FromResult(size);
        }
    }

    private sealed class ChunkedReader(string data, int maximumChunk) : TextReader
    {
        private int position;

        public override ValueTask<int> ReadAsync(
            Memory<char> destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position >= data.Length)
                return ValueTask.FromResult(0);
            var size = Math.Min(maximumChunk,
                Math.Min(destination.Length, data.Length - position));
            data.AsMemory(position, size).CopyTo(destination);
            position += size;
            return ValueTask.FromResult(size);
        }
    }

    private sealed class StalledReader : TextReader
    {
        public override async ValueTask<int> ReadAsync(
            Memory<char> destination, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
