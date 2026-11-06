using System.Text;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Conservative Windows INF logical-line reader. INF uses a trailing
/// backslash to continue a directive and semicolons for comments outside
/// quoted strings / %string-key% tokens. The text is only inspected, never
/// executed or passed to Windows installation APIs.
/// </summary>
public static class InfLogicalLines
{
    public const int MaxInfCharacters = OemZipInfEvidence.MaxInfBytes;
    public const int MaxLogicalLineCharacters = 32 * 1024;
    public const int MaxContinuations = 64;

    public static IEnumerable<string> Read(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaxInfCharacters)
            throw new InvalidDataException("INF oltre il limite di 4 MiB.");

        var pending = new StringBuilder();
        var continuations = 0;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
                continue;

            var continued = IsContinued(line);
            var fragment = continued ? line[..^1].TrimEnd() : line;
            if (pending.Length + fragment.Length > MaxLogicalLineCharacters)
                throw new InvalidDataException(
                    "Direttiva INF oltre il limite di riga logica.");

            pending.Append(fragment);
            if (continued)
            {
                if (++continuations > MaxContinuations)
                    throw new InvalidDataException(
                        "Troppe continuazioni di riga in un INF.");
                continue;
            }

            yield return pending.ToString();
            pending.Clear();
            continuations = 0;
        }

        // Do not turn a truncated directive into usable PnP evidence.
        if (pending.Length > 0 || continuations > 0)
            throw new InvalidDataException(
                "Direttiva INF con continuazione finale incompleta.");
    }

    private static string StripComment(string line)
    {
        var quoted = false;
        var stringToken = false;
        for (var i = 0; i < line.Length; i++)
        {
            switch (line[i])
            {
                case '"' when !stringToken:
                    quoted = !quoted;
                    break;
                case '%' when !quoted:
                    stringToken = !stringToken;
                    break;
                case ';' when !quoted && !stringToken:
                    return line[..i];
            }
        }

        return line;
    }

    private static bool IsContinued(string line)
    {
        if (!line.EndsWith('\\'))
            return false;
        // A path or text value ending in a backslash INSIDE a quoted value
        // is not a continuation. A closing quote followed by \ is.
        var quoted = false;
        foreach (var c in line)
        {
            if (c == '"')
                quoted = !quoted;
        }

        return !quoted;
    }
}
