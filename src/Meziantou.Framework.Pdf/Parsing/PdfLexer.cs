using System.Buffers;
using System.Globalization;

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
        var length = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '#' && i + 2 < raw.Length && TryHexValue(raw[i + 1], out var high) && TryHexValue(raw[i + 2], out var low))
            {
                result[length++] = (byte)((high << 4) | low);
                i += 2;
            }
            else
            {
                result[length++] = raw[i];
            }
        }

        return result.AsSpan(0, length).ToArray();
    }

    /// <summary>Decodes a literal string token (7.3.4.2).</summary>
    public byte[] DecodeLiteralString(PdfToken token)
    {
        var raw = Span[(token.Start + 1)..token.End];
        if (raw.Length > 0 && raw[^1] == ')')
        {
            raw = raw[..^1];
        }

        var output = new ArrayBufferWriter<byte>(Math.Max(1, raw.Length));
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
                        case (byte)'n': Write(output, (byte)'\n'); break;
                        case (byte)'r': Write(output, (byte)'\r'); break;
                        case (byte)'t': Write(output, (byte)'\t'); break;
                        case (byte)'b': Write(output, (byte)'\b'); break;
                        case (byte)'f': Write(output, (byte)'\f'); break;
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

                            Write(output, (byte)value);
                            break;
                        default:
                            // An unknown escape is the character itself
                            Write(output, c);
                            break;
                    }

                    break;
                case (byte)'\r':
                    // An end of line in a string is a line feed, whatever it is in the file
                    Write(output, (byte)'\n');
                    if (i + 1 < raw.Length && raw[i + 1] == '\n')
                    {
                        i++;
                    }

                    break;
                default:
                    Write(output, c);
                    break;
            }
        }

        return output.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a hexadecimal string token (7.3.4.3): whitespace and invalid characters are ignored, a missing last digit is 0.</summary>
    public byte[] DecodeHexString(PdfToken token)
    {
        var raw = Span[(token.Start + 1)..token.End];
        if (raw.Length > 0 && raw[^1] == '>')
        {
            raw = raw[..^1];
        }

        var result = new byte[(raw.Length + 1) / 2];
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
                result[length++] = (byte)((high << 4) | digit);
                high = -1;
            }
        }

        if (high >= 0)
        {
            result[length++] = (byte)(high << 4);
        }

        return result.AsSpan(0, length).ToArray();
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
    public static bool TryParseNumber(ReadOnlySpan<byte> text, out bool isInteger, out long integer, out double real)
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
        Span<byte> clean = stackalloc byte[Math.Min(text.Length + 2, 512)];
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

        var cleanSpan = clean[..cleanLength];
        if (isInteger)
        {
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

    private static void Write(ArrayBufferWriter<byte> output, byte value)
    {
        output.GetSpan(1)[0] = value;
        output.Advance(1);
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
