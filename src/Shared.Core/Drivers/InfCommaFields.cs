using System.Text;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Bounded INF comma-field lexer shared by OEM Models/Manufacturer identity
/// parsing and SourceDisks/CopyFiles payload auditing. A quote is valid ONLY
/// around an entire field; a comma inside quotes belongs to that field.
/// Doubled quotes represent a literal quote, never punctuation to trim away.
/// Malformed input returns null so callers can fail closed in their domain.
/// </summary>
internal static class InfCommaFields
{
    public static string[]? TrySplit(string value, int maximumFields = 8)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (maximumFields <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumFields));

        var fields = new List<string>(5);
        var field = new StringBuilder();
        var insideQuotes = false;
        var afterQuote = false;

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (insideQuotes)
            {
                if (ch != '"')
                {
                    field.Append(ch);
                    continue;
                }
                if (i + 1 < value.Length && value[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                    continue;
                }
                insideQuotes = false;
                afterQuote = true;
                continue;
            }

            if (ch == ',')
            {
                if (fields.Count >= maximumFields - 1)
                    return null;
                fields.Add(field.ToString().Trim());
                field.Clear();
                afterQuote = false;
                continue;
            }
            if (ch == '"')
            {
                // A field may begin with quote after whitespace, not
                // contain quote in an unquoted token or after its end.
                if (afterQuote || field.ToString().Trim().Length != 0)
                    return null;
                insideQuotes = true;
                continue;
            }
            if (afterQuote && !char.IsWhiteSpace(ch))
                return null;

            field.Append(ch);
        }

        if (insideQuotes)
            return null;

        fields.Add(field.ToString().Trim());
        return fields.ToArray();
    }
}
