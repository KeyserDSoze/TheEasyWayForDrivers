using System.IO;
using System.Text;

namespace TheEasyWayForDrivers.ServiceApp.Ipc;

/// <summary>
/// Reads a single newline-delimited IPC request with a hard bound on retained
/// characters. Only one request is processed per pipe connection.
/// </summary>
public static class BoundedIpcRequestReader
{
    public readonly record struct ReadResult(string? Line, bool TooLarge);

    public static async Task<ReadResult> ReadLineAsync(
        TextReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);
        cancellationToken.ThrowIfCancellationRequested();

        var line = new StringBuilder(Math.Min(maximumCharacters, 1024));
        var buffer = new char[1];

        while (true)
        {
            // Read one character at a time: a chunked read could consume bytes
            // past the first newline and would complicate protocol framing.
            // StreamReader buffers the underlying pipe, so this is bounded
            // without performing one OS read for every character.
            var count = await reader.ReadAsync(
                buffer.AsMemory(), cancellationToken);
            if (count == 0)
                return new ReadResult(
                    line.Length == 0 ? null : line.ToString(), false);

            var character = buffer[0];
            if (character is '\r' or '\n')
                return new ReadResult(line.ToString(), false);

            // Permit exactly maximumCharacters; reject the first extra char
            // without appending it, and never return the partial JSON.
            if (line.Length == maximumCharacters)
                return new ReadResult(null, true);

            line.Append(character);
        }
    }
}
