using System.IO;
using System.Text;

namespace TheEasyWayForDrivers.Core.Ipc;

/// <summary>
/// Bounded, buffered reader for newline-delimited Desktop/Service IPC frames.
/// One instance is reused for an entire pipe connection so a chunk holding
/// progress AND result messages cannot discard the second message.
/// </summary>
public sealed class BoundedIpcResponseReader
{
    // A large Windows driver inventory/search report may legitimately be
    // many megabytes. Enforce a per-frame cap rather than a total search
    // timeout, while never buffering unbounded input before JSON parsing.
    public const int MaximumMessageCharacters = 32 * 1024 * 1024;

    private readonly TextReader reader;
    private readonly int maximumCharacters;
    private readonly char[] buffer = new char[4096];
    private int position;
    private int count;
    private bool skipLineFeedAfterCarriageReturn;

    public BoundedIpcResponseReader(
        TextReader reader, int maximumCharacters = MaximumMessageCharacters)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);
        this.reader = reader;
        this.maximumCharacters = maximumCharacters;
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var line = new StringBuilder(Math.Min(maximumCharacters, 1024));

        while (true)
        {
            if (position == count)
            {
                count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                position = 0;
                if (count == 0)
                    return line.Length == 0 ? null : line.ToString();
            }

            var character = buffer[position++];

            // A CRLF can straddle two reads. Suppress exactly one LF, but
            // never discard the first character of the next IPC frame.
            if (skipLineFeedAfterCarriageReturn)
            {
                skipLineFeedAfterCarriageReturn = false;
                if (character == '\n')
                    continue;
            }

            if (character == '\r')
            {
                skipLineFeedAfterCarriageReturn = true;
                return line.ToString();
            }

            if (character == '\n')
                return line.ToString();

            if (line.Length >= maximumCharacters)
                throw new InvalidDataException(
                    "IPC response exceeds the maximum permitted size.");

            line.Append(character);
        }
    }
}
