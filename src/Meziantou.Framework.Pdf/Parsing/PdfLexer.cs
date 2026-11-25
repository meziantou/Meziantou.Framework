using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>
/// Splits PDF data into tokens (ISO 32000-2, 7.2 and 7.3). The lexer accepts what readers usually accept: numbers with
/// several signs or points, unbalanced string escapes, invalid characters in hexadecimal strings.
/// </summary>
internal sealed class PdfLexer
{
    private const byte Regular = 0;
    private const byte Whitespace = 1;
    private const byte Delimiter = 2;

    /// <summary>The number of significant characters of a number that are parsed: more is beyond the precision or the range of a <see cref="double"/>.</summary>
    internal const int MaxSignificantNumberLength = 512;

    private static readonly byte[] CharacterClasses = CreateCharacterClasses();

    private readonly ReadOnlyMemory<byte> _data;

    public PdfLexer(ReadOnlyMemory<byte> data, int position = 0)
    {
        _data = data;
        Position = position;
    }

    public ReadOnlyMemory<byte> Data => _data;

    public ReadOnlySpan<byte> Span => _data.Span;

    public int Length => _data.Length;

    public int Position { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a token reached the end of the data: when the data is a window of a larger
    /// input, the token may continue after it, and the object must be parsed again with a larger window.
    /// </summary>
    public bool ReachedEnd { get; set; }

    public static bool IsWhitespace(byte value) => CharacterClasses[value] == Whitespace;

    public static bool IsDelimiter(byte value) => CharacterClasses[value] == Delimiter;

    public static bool IsRegular(byte value) => CharacterClasses[value] == Regular;

    public PdfToken Peek()
    {
        var position = Position;
        var token = Next();
        Position = position;
        return token;
    }

    public PdfToken Next()
    {
        var token = NextCore();
        if (Position >= _data.Length)
        {
            ReachedEnd = true;
        }

        return token;
    }

    private PdfToken NextCore()
    {
        SkipWhitespaceAndComments();
        var span = Span;
        var start = Position;
        if (start >= span.Length)
            return new PdfToken(PdfTokenKind.EndOfInput, start, start);

        var c = span[start];
        switch (c)
        {
            case (byte)'[':
                Position++;
                return new PdfToken(PdfTokenKind.ArrayStart, start, Position);
            case (byte)']':
                Position++;
                return new PdfToken(PdfTokenKind.ArrayEnd, start, Position);
            case (byte)'{':
                Position++;
                return new PdfToken(PdfTokenKind.ProcedureStart, start, Position);
            case (byte)'}':
                Position++;
                return new PdfToken(PdfTokenKind.ProcedureEnd, start, Position);
            case (byte)'(':
                SkipLiteralString();
                return new PdfToken(PdfTokenKind.LiteralString, start, Position);
            case (byte)'/':
                Position++;
                while (Position < span.Length && IsRegular(span[Position]))
                {
                    Position++;
                }

                return new PdfToken(PdfTokenKind.Name, start, Position);
            case (byte)'<':
                if (start + 1 < span.Length && span[start + 1] == '<')
                {
                    Position += 2;
                    return new PdfToken(PdfTokenKind.DictionaryStart, start, Position);
                }

                Position++;
                while (Position < span.Length && span[Position] != '>')
                {
                    Position++;
                }

                if (Position < span.Length)
                {
                    Position++;
                }

                return new PdfToken(PdfTokenKind.HexString, start, Position);
            case (byte)'>':
                if (start + 1 < span.Length && span[start + 1] == '>')
                {
                    Position += 2;
                    return new PdfToken(PdfTokenKind.DictionaryEnd, start, Position);
                }

                Position++;
                return new PdfToken(PdfTokenKind.Invalid, start, Position);
            case (byte)')':
                Position++;
                return new PdfToken(PdfTokenKind.Invalid, start, Position);
        }

        while (Position < span.Length && IsRegular(span[Position]))
        {
            Position++;
        }

        var text = span[start..Position];
        if (c is (byte)'+' or (byte)'-' or (byte)'.' or (>= (byte)'0' and <= (byte)'9') && TryParseNumber(text, out var isInteger, out var integer, out var real))
            return isInteger ? new PdfToken(PdfTokenKind.Integer, start, Position, integer, integer) : new PdfToken(PdfTokenKind.Real, start, Position, RealValue: real);

        return new PdfToken(PdfTokenKind.Keyword, start, Position);
    }

    public void SkipWhitespaceAndComments()
    {
        var span = Span;
        while (Position < span.Length)
        {
            var c = span[Position];
            if (IsWhitespace(c))
            {
                Position++;
            }
            else if (c == '%')
            {
                while (Position < span.Length && span[Position] is not ((byte)'\r' or (byte)'\n'))
                {
                    Position++;
                }
            }
            else
            {
                break;
            }
        }
    }

    public void SkipWhitespace()
    {
        var span = Span;
        while (Position < span.Length && IsWhitespace(span[Position]))
        {
            Position++;
        }
    }

    public bool IsKeyword(PdfToken token, ReadOnlySpan<byte> keyword) => token.Kind == PdfTokenKind.Keyword && Span[token.Start..token.End].SequenceEqual(keyword);

    public ReadOnlySpan<byte> GetBytes(PdfToken token) => Span[token.Start..token.End];

    /// <summary>Decodes the bytes of a name token (without the slash), with its <c>#xx</c> escapes.</summary>
    public byte[] DecodeName(PdfToken token)
    {
        var raw = Span[(token.Start + 1)..token.End];
        if (raw.IndexOf((byte)'#') < 0)
            return raw.ToArray();

        var result = new byte[raw.Length];
        return result.AsSpan(0, DecodeName(raw, result)).ToArray();
    }

    /// <summary>Gets the length of the bytes of a name token once decoded: at most the length of the token without the slash.</summary>
    public static int GetNameLength(PdfToken token) => token.Length - 1;

    /// <summary>Decodes the bytes of a name token (without the slash) to a buffer as long as <see cref="GetNameLength"/> at least.</summary>
    /// <returns>The number of bytes written.</returns>
    public int DecodeName(PdfToken token, Span<byte> destination) => DecodeName(Span[(token.Start + 1)..token.End], destination);

    private static int DecodeName(ReadOnlySpan<byte> raw, Span<byte> destination)
    {
        var length = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '#' && i + 2 < raw.Length && TryHexValue(raw[i + 1], out var high) && TryHexValue(raw[i + 2], out var low))
            {
                destination[length++] = (byte)((high << 4) | low);
                i += 2;
            }
            else
            {
                destination[length++] = raw[i];
            }
        }

        return length;
    }

    /// <summary>Decodes a literal string token (7.3.4.2).</summary>
    public byte[] DecodeLiteralString(PdfToken token)
    {
        var raw = GetLiteralStringBody(token);

        // Most strings have no escape and no end of line to normalize: their bytes are the string
        if (!NeedsLiteralStringDecoding(raw))
            return raw.ToArray();

        var buffer = raw.Length <= 256 ? stackalloc byte[256] : new byte[raw.Length];
        return buffer[..DecodeLiteralString(raw, buffer)].ToArray();
    }

    /// <summary>
    /// Decodes a literal string token (7.3.4.2) without allocating: the bytes are those of the data when the string has
    /// no escape and no end of line to normalize, otherwise they are decoded to <paramref name="buffer"/>, which grows as needed.
    /// </summary>
    /// <returns>The bytes of the string, valid until the buffer is used again.</returns>
    public ReadOnlySpan<byte> DecodeLiteralString(PdfToken token, ref byte[]? buffer)
    {
        var raw = GetLiteralStringBody(token);
        if (!NeedsLiteralStringDecoding(raw))
            return raw;

        var destination = GetBuffer(ref buffer, raw.Length);
        return destination[..DecodeLiteralString(raw, destination)];
    }

    /// <summary>Decodes a hexadecimal string token (7.3.4.3): whitespace and invalid characters are ignored, a missing last digit is 0.</summary>
    public byte[] DecodeHexString(PdfToken token)
    {
        var raw = GetHexStringBody(token);
        var result = new byte[(raw.Length + 1) / 2];
        var length = DecodeHexString(raw, result);
        return length == result.Length ? result : result.AsSpan(0, length).ToArray();
    }

    /// <summary>Decodes a hexadecimal string token (7.3.4.3) to <paramref name="buffer"/>, which grows as needed.</summary>
    /// <returns>The bytes of the string, valid until the buffer is used again.</returns>
    public ReadOnlySpan<byte> DecodeHexString(PdfToken token, ref byte[]? buffer)
    {
        var raw = GetHexStringBody(token);
        var destination = GetBuffer(ref buffer, (raw.Length + 1) / 2);
        return destination[..DecodeHexString(raw, destination)];
    }

    private ReadOnlySpan<byte> GetLiteralStringBody(PdfToken token)
    {
        var raw = Span[(token.Start + 1)..token.End];
        if (raw.Length > 0 && raw[^1] == ')')
        {
            raw = raw[..^1];
        }

        return raw;
    }

    private ReadOnlySpan<byte> GetHexStringBody(PdfToken token)
    {
        var raw = Span[(token.Start + 1)..token.End];
        if (raw.Length > 0 && raw[^1] == '>')
        {
            raw = raw[..^1];
        }

        return raw;
    }

    private static Span<byte> GetBuffer(ref byte[]? buffer, int length)
    {
        if (buffer is null || buffer.Length < length)
        {
            buffer = new byte[Math.Max(length, (int)Math.Min(Array.MaxLength, BitOperations.RoundUpToPowerOf2((uint)Math.Max(length, 256))))];
        }

        return buffer;
    }

    private static bool NeedsLiteralStringDecoding(ReadOnlySpan<byte> raw) => raw.IndexOfAny((byte)'\\', (byte)'\r') >= 0;

    /// <summary>Decodes the bytes between the parentheses of a literal string to a buffer as long as them at least: each byte gives one byte at most.</summary>
    private static int DecodeLiteralString(ReadOnlySpan<byte> raw, Span<byte> destination)
    {
        var length = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            switch (c)
            {
                case (byte)'\\':
                    i++;
                    if (i >= raw.Length)
                        break;

                    c = raw[i];
                    switch (c)
                    {
                        case (byte)'n': destination[length++] = (byte)'\n'; break;
                        case (byte)'r': destination[length++] = (byte)'\r'; break;
                        case (byte)'t': destination[length++] = (byte)'\t'; break;
                        case (byte)'b': destination[length++] = (byte)'\b'; break;
                        case (byte)'f': destination[length++] = (byte)'\f'; break;
                        case (byte)'\r':
                            // A backslash at the end of a line continues the string on the next line
                            if (i + 1 < raw.Length && raw[i + 1] == '\n')
                            {
                                i++;
                            }

                            break;
                        case (byte)'\n':
                            break;
                        case >= (byte)'0' and <= (byte)'7':
                            var value = c - '0';
                            for (var digits = 1; digits < 3 && i + 1 < raw.Length && raw[i + 1] is >= (byte)'0' and <= (byte)'7'; digits++)
                            {
                                i++;
                                value = (value * 8) + (raw[i] - '0');
                            }

                            destination[length++] = (byte)value;
                            break;
                        default:
                            // An unknown escape is the character itself
                            destination[length++] = c;
                            break;
                    }

                    break;
                case (byte)'\r':
                    // An end of line in a string is a line feed, whatever it is in the file
                    destination[length++] = (byte)'\n';
                    if (i + 1 < raw.Length && raw[i + 1] == '\n')
                    {
                        i++;
                    }

                    break;
                default:
                    destination[length++] = c;
                    break;
            }
        }

        return length;
    }

    /// <summary>Decodes the digits of a hexadecimal string to a buffer of half their length (rounded up) at least.</summary>
    private static int DecodeHexString(ReadOnlySpan<byte> raw, Span<byte> destination)
    {
        var length = 0;
        var high = -1;
        foreach (var c in raw)
        {
            if (!TryHexValue(c, out var digit))
                continue;

            if (high < 0)
            {
                high = digit;
            }
            else
            {
                destination[length++] = (byte)((high << 4) | digit);
                high = -1;
            }
        }

        if (high >= 0)
        {
            destination[length++] = (byte)(high << 4);
        }

        return length;
    }

    public static bool TryHexValue(byte c, out int value)
    {
        switch (c)
        {
            case >= (byte)'0' and <= (byte)'9':
                value = c - '0';
                return true;
            case >= (byte)'a' and <= (byte)'f':
                value = c - 'a' + 10;
                return true;
            case >= (byte)'A' and <= (byte)'F':
                value = c - 'A' + 10;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    /// <summary>Parses a number leniently: several leading signs, several points, or no digit at all (0), as readers do.</summary>
    /// <remarks>
    /// A token of any length is parsed in time proportional to its length, and its value is the value it represents
    /// within the precision of a <see cref="double"/>: the leading zeros are skipped, an integer that does not fit in a
    /// <see cref="long"/> is a real, a real beyond the range of a <see cref="double"/> saturates to
    /// <see cref="double.MaxValue"/> (or its opposite), and the decimal digits beyond <see cref="MaxSignificantNumberLength"/>
    /// characters, far below the precision of a <see cref="double"/>, are ignored.
    /// </remarks>
    public static bool TryParseNumber(ReadOnlySpan<byte> text, out bool isInteger, out long integer, out double real)
    {
        var i = 0;
        var negative = false;
        while (i < text.Length && text[i] is (byte)'+' or (byte)'-')
        {
            negative |= text[i] == '-';
            i++;
        }

        if (TryParseShortNumber(text[i..], negative, out isInteger, out integer, out real))
            return true;

        return TryParseAnyNumber(text, out isInteger, out integer, out real);
    }

    /// <summary>The general parser of <see cref="TryParseNumber"/>: any token, whatever its length.</summary>
    internal static bool TryParseAnyNumber(ReadOnlySpan<byte> text, out bool isInteger, out long integer, out double real)
    {
        isInteger = true;
        integer = 0;
        real = 0;
        var i = 0;
        var negative = false;
        while (i < text.Length && text[i] is (byte)'+' or (byte)'-')
        {
            negative |= text[i] == '-';
            i++;
        }

        var digitsStart = i;
        var hasDigit = false;
        var pointSeen = false;
        var integerDigits = 0;
        Span<byte> clean = stackalloc byte[Math.Min(text.Length + 2, MaxSignificantNumberLength + 2)];
        var cleanLength = 0;
        if (negative)
        {
            clean[cleanLength++] = (byte)'-';
        }

        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (c is >= (byte)'0' and <= (byte)'9')
            {
                hasDigit = true;
                if (!pointSeen)
                {
                    // The leading zeros of the integer part do not change the value
                    if (c == '0' && integerDigits == 0)
                        continue;

                    integerDigits++;
                }

                if (cleanLength < clean.Length)
                {
                    clean[cleanLength++] = c;
                }
            }
            else if (c == '.' && !pointSeen)
            {
                pointSeen = true;
                isInteger = false;
                if (cleanLength < clean.Length)
                {
                    clean[cleanLength++] = c;
                }
            }
            else if (c is (byte)'.' or (byte)'-' or (byte)'+')
            {
                // A second point, or a sign in the middle ("4.-5", "0.0.0"): the rest is ignored
                break;
            }
            else
            {
                // A letter: this is not a number ("1e5" is not a PDF number either)
                return false;
            }
        }

        if (i < text.Length)
        {
            for (var j = i; j < text.Length; j++)
            {
                if (text[j] is not ((>= (byte)'0' and <= (byte)'9') or (byte)'.' or (byte)'-' or (byte)'+'))
                    return false;
            }
        }

        if (!hasDigit)
        {
            // "-" or "." alone are read as 0
            if (digitsStart == text.Length || text[digitsStart..].IndexOfAnyExcept((byte)'.', (byte)'-') < 0)
                return true;

            return false;
        }

        // An integer part longer than the buffer is beyond the range of a double: it saturates, whatever its digits
        if (integerDigits > MaxSignificantNumberLength)
        {
            isInteger = false;
            real = negative ? double.MinValue : double.MaxValue;
            return true;
        }

        var cleanSpan = clean[..cleanLength];
        if (isInteger)
        {
            if (cleanSpan.Length == 0 || (cleanSpan.Length == 1 && cleanSpan[0] == '-'))
                return true;

            if (long.TryParse(cleanSpan, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out integer))
                return true;

            isInteger = false;
        }

        if (cleanSpan.Length > 0 && cleanSpan[^1] == '.')
        {
            cleanSpan = cleanSpan[..^1];
        }

        if (cleanSpan.Length == 0 || (cleanSpan.Length == 1 && cleanSpan[0] == '-'))
        {
            real = 0;
            return true;
        }

        real = double.Parse(cleanSpan, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (!double.IsFinite(real))
        {
            real = negative ? double.MinValue : double.MaxValue;
        }

        return true;
    }

    /// <summary>
    /// Parses the numbers of most files without the general parser: digits with at most one point, and at most 15 digits.
    /// The digits then make an integer below 2^53, and the value of a real is that integer divided by a power of ten of
    /// at most 10^15: both are exact doubles, so the quotient is the correctly rounded value, as <see cref="double.Parse(ReadOnlySpan{byte}, NumberStyles, IFormatProvider?)"/> gives.
    /// <paramref name="digits"/> is the token after its signs; <see langword="false"/> leaves the token to the general parser.
    /// </summary>
    private static bool TryParseShortNumber(ReadOnlySpan<byte> digits, bool negative, out bool isInteger, out long integer, out double real)
    {
        const int MaxDigits = 15;

        isInteger = true;
        integer = 0;
        real = 0;
        if (digits.IsEmpty || digits.Length > MaxDigits + 1)
            return false;

        var mantissa = 0L;
        var digitCount = 0;
        var pointIndex = -1;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = (uint)(digits[i] - '0');
            if (digit <= 9)
            {
                mantissa = (mantissa * 10) + digit;
                digitCount++;
            }
            else if (digits[i] == '.' && pointIndex < 0)
            {
                pointIndex = i;
            }
            else
            {
                return false;
            }
        }

        if (digitCount is 0 or > MaxDigits)
            return false;

        if (pointIndex < 0)
        {
            integer = negative ? -mantissa : mantissa;
            return true;
        }

        isInteger = false;
        var decimals = digits.Length - 1 - pointIndex;
        var value = (double)mantissa / PowersOfTen[decimals];

        // The sign of zero is that of the general parser: kept with decimal digits ("-0.0"), not without ("-0.")
        real = negative && (mantissa != 0 || decimals > 0) ? -value : value;
        return true;
    }

    private static ReadOnlySpan<double> PowersOfTen => [1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15];

    /// <summary>Finds the next occurrence of a keyword delimited by non-regular characters.</summary>
    public int IndexOfKeyword(ReadOnlySpan<byte> keyword, int start)
    {
        var span = Span;
        var position = start;
        while (position < span.Length)
        {
            var index = span[position..].IndexOf(keyword);
            if (index < 0)
                return -1;

            var found = position + index;
            var end = found + keyword.Length;
            if ((found == 0 || !IsRegular(span[found - 1])) && (end >= span.Length || !IsRegular(span[end])))
                return found;

            position = found + 1;
        }

        return -1;
    }

    private void SkipLiteralString()
    {
        var span = Span;
        Position++;
        var depth = 1;
        while (Position < span.Length)
        {
            var c = span[Position++];
            if (c == '\\')
            {
                Position++;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                    return;
            }
        }

        Position = Math.Min(Position, span.Length);
    }

    private static byte[] CreateCharacterClasses()
    {
        var classes = new byte[256];
        foreach (var c in "\0\t\n\f\r ")
        {
            classes[c] = Whitespace;
        }

        foreach (var c in "()<>[]{}/%")
        {
            classes[c] = Delimiter;
        }

        return classes;
    }
}
