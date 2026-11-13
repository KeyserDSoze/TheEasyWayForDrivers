using System.IO;
using TheEasyWayForDrivers.ServiceApp.Ipc;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// Regression for #431: IPC request size must be checked before ReadLineAsync
/// could buffer an arbitrarily long, unterminated client payload.
/// </summary>
public sealed class BoundedIpcRequestReaderTests
{
    [Theory]
    [InlineData("{\"command\":\"scan\"}\n", "{\"command\":\"scan\"}")]
    [InlineData("{\"command\":\"scan\"}\r\n", "{\"command\":\"scan\"}")]
    [InlineData("{\"command\":\"scan\"}\r", "{\"command\":\"scan\"}")]
    [InlineData("{\"command\":\"scan\"}", "{\"command\":\"scan\"}")]
    [InlineData("\n", "")]
    public async Task StandardDesktopJsonFramingIsPreserved(
        string payload, string expected)
    {
        using var reader = new StringReader(payload);

        var result = await BoundedIpcRequestReader.ReadLineAsync(
            reader, 64 * 1024, CancellationToken.None);

        Assert.False(result.TooLarge);
        Assert.Equal(expected, result.Line);
    }

    [Fact]
    public async Task EmptyEofIsDifferentFromBlankLine()
    {
        using var reader = new StringReader(string.Empty);
        var result = await BoundedIpcRequestReader.ReadLineAsync(
            reader, 64, CancellationToken.None);

        Assert.Null(result.Line);
        Assert.False(result.TooLarge);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public async Task ExactCharacterLimitIsAccepted(string terminator)
    {
        using var reader = new StringReader(
            new string('x', 64 * 1024) + terminator);
        var result = await BoundedIpcRequestReader.ReadLineAsync(
            reader, 64 * 1024, CancellationToken.None);

        Assert.False(result.TooLarge);
        Assert.Equal(64 * 1024, result.Line?.Length);
    }

    [Fact]
    public async Task OversizedUnterminatedInputStopsAtFirstExtraCharacter()
    {
        using var reader = new CountingReader(new string('x', 1024 * 1024));
        var result = await BoundedIpcRequestReader.ReadLineAsync(
            reader, 64 * 1024, CancellationToken.None);

        Assert.True(result.TooLarge);
        Assert.Null(result.Line);
        Assert.Equal(64 * 1024 + 1, reader.CharactersRead);
    }

    [Fact]
    public async Task OversizedWhitespaceIsAlsoRejectedWithoutRetainingContent()
    {
        using var reader = new StringReader(new string(' ', 65) + "\n");
        var result = await BoundedIpcRequestReader.ReadLineAsync(
            reader, 64, CancellationToken.None);

        Assert.True(result.TooLarge);
        Assert.Null(result.Line);
    }

    [Fact]
    public async Task CancellationIsHonoredBeforeAnyInput()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var reader = new CountingReader("sensitive request");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await BoundedIpcRequestReader.ReadLineAsync(
                reader, 64, source.Token));

        Assert.Equal(0, reader.CharactersRead);
    }

    private sealed class CountingReader(string payload) : TextReader
    {
        private int position;

        public int CharactersRead => position;

        public override ValueTask<int> ReadAsync(
            Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position == payload.Length)
                return ValueTask.FromResult(0);

            payload.AsMemory(position, 1).CopyTo(buffer);
            position++;
            return ValueTask.FromResult(1);
        }
    }
}
