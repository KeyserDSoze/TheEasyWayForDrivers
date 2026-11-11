using System.Text;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Conservatively reads INF text for read-only PnP evidence. Windows INF
/// supports ANSI and UTF-16, but Windows' ANSI code page is locale-specific.
/// We recover only the ASCII grammar/IDs from unlabelled legacy bytes; we
/// deliberately do NOT claim to decode OEM localized names correctly.
/// No result from this class can authorize driver installation.
/// </summary>
public static class OemInfTextDecoder
{
    public enum Kind
    {
        Utf8,
        Utf8Bom,
        Utf16LittleEndian,
        Utf16BigEndian,
        Utf16LittleEndianNoBom,
        Utf16BigEndianNoBom,
        LegacyAnsiAsciiOnly
    }

    public sealed record Result(string Text, Kind Encoding)
    {
        public bool IsLegacyFallback => Encoding == Kind.LegacyAnsiAsciiOnly;
    }

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16Le = new(false, true, true);
    private static readonly UnicodeEncoding StrictUtf16Be = new(true, true, true);

    /// <summary>
    /// Load a user-selected INF with the SAME bounded decoder as ZIP/CAB.
    /// Stream length is checked before allocation and every read is limited,
    /// so a file growing between metadata check and read fails closed.
    /// </summary>
    public static Result ReadBounded(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek ||
            stream.Length is < 1 or > OemZipInfEvidence.MaxInfBytes)
            throw new InvalidDataException("Dimensione INF non ammessa.");

        using var memory = new MemoryStream((int)stream.Length);
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (memory.Length + read > OemZipInfEvidence.MaxInfBytes)
                throw new InvalidDataException("INF oltre il limite di 4 MiB.");
            memory.Write(buffer, 0, read);
        }
        return Decode(memory.ToArray());
    }

    public static Result Decode(ReadOnlySpan<byte> content)
    {
        if (content.Length is < 1 or > OemZipInfEvidence.MaxInfBytes)
            throw new InvalidDataException("Dimensione INF non ammessa.");

        string text;
        Kind kind;

        try
        {
            // Reject UTF-32 (and other unsupported Unicode formats) before
            // interpreting its first two bytes as UTF-16 or ASCII.
            if (StartsWith(content, 0xFF, 0xFE, 0x00, 0x00) ||
                StartsWith(content, 0x00, 0x00, 0xFE, 0xFF))
                throw new InvalidDataException("INF UTF-32 non supportato.");

            if (StartsWith(content, 0xFF, 0xFE))
            {
                text = StrictUtf16Le.GetString(content[2..]);
                kind = Kind.Utf16LittleEndian;
            }
            else if (StartsWith(content, 0xFE, 0xFF))
            {
                text = StrictUtf16Be.GetString(content[2..]);
                kind = Kind.Utf16BigEndian;
            }
            else if (StartsWith(content, 0xEF, 0xBB, 0xBF))
            {
                text = StrictUtf8.GetString(content[3..]);
                kind = Kind.Utf8Bom;
            }
            else if (TryDecodeUnmarkedUtf16(content, out var unmarkedText,
                out var unmarkedKind))
            {
                // A sufficiently clear UTF-16 byte pattern AND actual
                // Windows INF grammar are required; ambiguous/binary data
                // still reaches the normal UTF-8/control rejection path.
                text = unmarkedText;
                kind = unmarkedKind;
            }
            else
            {
                try
                {
                    text = StrictUtf8.GetString(content);
                    kind = Kind.Utf8;
                }
                catch (DecoderFallbackException)
                {
                    // A genuine legacy INF may contain 8-bit localized text
                    // while its [Version]/[Manufacturer]/[Models] grammar and
                    // PnP identifiers remain ASCII. Never guess CP1252/ACP:
                    // non-ASCII bytes are replaced solely for inspection.
                    // Only comments and [Strings] values may have such bytes.
                    if (!IsSafeLegacyInf(content))
                        throw new InvalidDataException(
                            "INF ANSI non interpretabile con sintassi ASCII sicura.");

                    var chars = new char[content.Length];
                    for (var i = 0; i < content.Length; i++)
                        chars[i] = content[i] < 0x80 ? (char)content[i] : '\uFFFD';
                    text = new string(chars);
                    kind = Kind.LegacyAnsiAsciiOnly;
                }
            }
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException("INF Unicode malformato.", error);
        }

        // Never pass NULs, embedded BOMs, bidi controls or binary/C0
        // control characters into the ID and DriverVer parsers.
        foreach (var c in text)
        {
            if (c is '\r' or '\n' or '\t')
                continue;
            if (char.IsControl(c) || c is '\uFEFF' or '\u202A' or '\u202B'
                    or '\u202C' or '\u202D' or '\u202E' or '\u2066'
                    or '\u2067' or '\u2068' or '\u2069')
                throw new InvalidDataException(
                    "INF contiene caratteri di controllo o dati binari.");
        }

        // Legacy fallback has intentionally lossy localized display values,
        // but a normal INF must at least contain a recognisable [Version]
        // section. Do not interpret arbitrary non-UTF8 bytes as driver data.
        if (kind == Kind.LegacyAnsiAsciiOnly &&
            !text.Contains("[Version]", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Contenuto ANSI senza intestazione INF verificabile.");

        return new Result(text, kind);
    }

    /// <summary>
    /// A small minority of OEM INF files omit the UTF-16 BOM. Treat this as
    /// read-only INF text ONLY with strong endian evidence from interleaved
    /// ASCII NULs and recognizable Windows INF grammar. Do NOT infer from
    /// NULs alone: a binary payload must never yield device evidence.
    /// </summary>
    private static bool TryDecodeUnmarkedUtf16(
        ReadOnlySpan<byte> content, out string text, out Kind kind)
    {
        text = string.Empty;
        kind = Kind.Utf8;
        if (content.Length < 64 || (content.Length & 1) != 0)
            return false;

        var pairs = Math.Min(content.Length / 2, 4096);
        var evenNuls = 0;
        var oddNuls = 0;
        for (var i = 0; i < pairs; i++)
        {
            if (content[2 * i] == 0) evenNuls++;
            if (content[2 * i + 1] == 0) oddNuls++;
        }

        // Allow non-ASCII localized INF strings, but require a convincing
        // ASCII grammar footprint on ONE byte lane and almost no NULs on
        // the other. Mixed-endian or random binary is never accepted.
        var le = oddNuls >= pairs / 2 && evenNuls <= pairs / 20;
        var be = evenNuls >= pairs / 2 && oddNuls <= pairs / 20;
        if (le == be)
            return false;

        try
        {
            var candidate = le
                ? StrictUtf16Le.GetString(content)
                : StrictUtf16Be.GetString(content);

            // Require actual Windows INF header/section grammar rather than
            // just a Unicode-looking binary stream. Driver-specific model
            // parsing will STILL independently require Manufacturer refs.
            if (!candidate.Contains("[Version]", StringComparison.OrdinalIgnoreCase) ||
                !candidate.Contains("[Manufacturer]", StringComparison.OrdinalIgnoreCase) ||
                !candidate.Contains("Signature", StringComparison.OrdinalIgnoreCase))
                return false;

            text = candidate;
            kind = le ? Kind.Utf16LittleEndianNoBom
                : Kind.Utf16BigEndianNoBom;
            return true;
        }
        catch (DecoderFallbackException)
        {
            // Invalid Unicode must not be repaired or treated as hardware.
            return false;
        }
    }

    private static bool StartsWith(ReadOnlySpan<byte> content,
        params byte[] prefix) => content.StartsWith(prefix);

    private static bool IsSafeLegacyInf(ReadOnlySpan<byte> bytes)
    {
        // Legacy text without BOM is accepted only when high-bit bytes are
        // restricted to comments or the value side of a [Strings] directive.
        // A non-ASCII byte in section headings, hardware IDs, Models or
        // installation directives would make matching ambiguous.
        var start = 0;
        var inStrings = false;
        for (var index = 0; index <= bytes.Length; index++)
        {
            if (index != bytes.Length && bytes[index] != '\n')
                continue;

            var line = bytes[start..index];
            start = index + 1;
            if (!IsSafeAsciiControlLine(line))
                return false;

            var cursor = 0;
            while (cursor < line.Length &&
                   line[cursor] is (byte)' ' or (byte)'\t')
                cursor++;
            if (cursor == line.Length)
                continue;

            if (line[cursor] == '[')
            {
                // A section header must be pure ASCII.
                var close = line.IndexOf((byte)']');
                if (close < 0 || line[..(close + 1)].IndexOfAnyInRange(
                    (byte)0x80, byte.MaxValue) >= 0)
                    return false;
                var section = Encoding.ASCII.GetString(
                    line[(cursor + 1)..close]).Trim();
                inStrings = section.Equals("Strings", StringComparison.OrdinalIgnoreCase) ||
                    section.StartsWith("Strings.", StringComparison.OrdinalIgnoreCase);
            }

            // A semicolon is an INF comment delimiter ONLY outside
            // quotes and %string-key% tokens (same rule used by
            // InfLogicalLines). The old IndexOf(';') wrongly ignored
            // bytes after e.g. "%Model;Alias%" in [Models], letting
            // non-ASCII ANSI hardware-ID syntax pass the safety gate.
            var comment = FindInfCommentStart(line);
            var syntax = comment < 0 ? line : line[..comment];
            var highByte = syntax.IndexOfAnyInRange((byte)0x80, byte.MaxValue);
            if (highByte < 0)
                continue;
            if (!inStrings)
                return false;

            var equal = syntax.IndexOf((byte)'=');
            // The key of [Strings] and the '=' must remain ASCII.
            if (equal < 0 || highByte <= equal)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Find a real INF comment, not a semicolon inside a quoted value
    /// or a %StringKey% token. This intentionally mirrors the lexical
    /// state machine in InfLogicalLines.StripComment without converting
    /// unknown OEM code-page bytes into Unicode first.
    /// </summary>
    private static int FindInfCommentStart(ReadOnlySpan<byte> line)
    {
        var quoted = false;
        var stringToken = false;
        for (var i = 0; i < line.Length; i++)
        {
            switch (line[i])
            {
                case (byte)'"' when !stringToken:
                    quoted = !quoted;
                    break;
                case (byte)'%' when !quoted:
                    stringToken = !stringToken;
                    break;
                case (byte)';' when !quoted && !stringToken:
                    return i;
            }
        }

        return -1;
    }

    private static bool IsSafeAsciiControlLine(ReadOnlySpan<byte> line)
    {
        foreach (var b in line)
        {
            if (b is (byte)'\r' or (byte)'\t')
                continue;
            if (b < 0x20 || b == 0x7F)
                return false;
        }
        return true;
    }
}
