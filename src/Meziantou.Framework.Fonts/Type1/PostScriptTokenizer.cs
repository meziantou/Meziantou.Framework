using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// Reads the tokens of the PostScript language (PostScript Language Reference, third edition, 3.2 "Syntax"): the
/// subset a Type 1 font program uses (Adobe Type 1 Font Format, 2 "Font Program Organization"). The binary data of a
/// charstring is not a token: the reader of the private dictionary takes it with <see cref="ReadBinary"/>.
/// </summary>
/// <remarks>Comments are skipped. Strings and hexadecimal strings are tokens whose text is decoded on request.</remarks>
internal ref struct PostScriptTokenizer
{
    private readonly ReadOnlySpan<byte> _data;

    public PostScriptTokenizer(ReadOnlySpan<byte> data)
    {
        _data = data;
    }

    public int Position { get; set; }

    public static bool IsWhiteSpace(byte value) => value is 0 or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'\r' or (byte)' ';

    private static bool IsDelimiter(byte value) => value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    /// <summary>Reads the next token; <see cref="PostScriptTokenKind.EndOfData"/> at the end of the data.</summary>
    public PostScriptToken Read()
    {
        while (true)
        {
            while (Position < _data.Length && IsWhiteSpace(_data[Position]))
            {
                Position++;
            }

            if (Position >= _data.Length)
                return new PostScriptToken(PostScriptTokenKind.EndOfData, _data.Length, 0);

            if (_data[Position] != '%')
                break;

            // A comment runs to the end of the line
            while (Position < _data.Length && _data[Position] is not ((byte)'\r' or (byte)'\n'))
            {
                Position++;
            }
        }

        var start = Position;
        var first = _data[Position++];
        switch (first)
        {
            case (byte)'[':
                return new PostScriptToken(PostScriptTokenKind.ArrayStart, start, 1);
            case (byte)']':
                return new PostScriptToken(PostScriptTokenKind.ArrayEnd, start, 1);
            case (byte)'{':
                return new PostScriptToken(PostScriptTokenKind.ProcedureStart, start, 1);
            case (byte)'}':
                return new PostScriptToken(PostScriptTokenKind.ProcedureEnd, start, 1);
            case (byte)'(':
                SkipString();
                return new PostScriptToken(PostScriptTokenKind.String, start + 1, Math.Max(0, Position - start - 2));
            case (byte)'<' when Position < _data.Length && _data[Position] == '<':
                Position++;
                return new PostScriptToken(PostScriptTokenKind.DictionaryStart, start, 2);
            case (byte)'>' when Position < _data.Length && _data[Position] == '>':
                Position++;
                return new PostScriptToken(PostScriptTokenKind.DictionaryEnd, start, 2);
            case (byte)'<':
            {
                // A hexadecimal string, or an ASCII base-85 string (<~ ... ~>), which a font does not use but which is skipped whole
                var end = _data[Position..].IndexOf((byte)'>');
                Position = end < 0 ? _data.Length : Position + end + 1;
                return new PostScriptToken(PostScriptTokenKind.HexString, start + 1, Math.Max(0, Position - start - 2));
            }

            case (byte)')' or (byte)'>':
                // A closing delimiter without its opening one is a name of one character, which an interpreter would reject
                return new PostScriptToken(PostScriptTokenKind.ExecutableName, start, 1);
            case (byte)'/':
            {
                // "//name" is an immediately evaluated name: a literal name for this reader
                if (Position < _data.Length && _data[Position] == '/')
                {
                    Position++;
                }

                var nameStart = Position;
                SkipRegular();
                return new PostScriptToken(PostScriptTokenKind.LiteralName, nameStart, Position - nameStart);
            }

            default:
            {
                SkipRegular();
                var text = _data[start..Position];
                return TryParseNumber(text, out var number)
                    ? new PostScriptToken(PostScriptTokenKind.Number, start, text.Length, number)
                    : new PostScriptToken(PostScriptTokenKind.ExecutableName, start, text.Length);
            }
        }
    }

    /// <summary>Reads the next token without consuming it.</summary>
    public PostScriptToken Peek()
    {
        var position = Position;
        var token = Read();
        Position = position;
        return token;
    }

    /// <summary>
    /// Reads the binary data that follows the name of the procedure that reads it (<c>RD</c> or <c>-|</c>): one space,
    /// then the bytes (Type 1 Font Format, 2.4 "Charstring encryption" and 6 "CharStrings Dictionary").
    /// </summary>
    public ReadOnlySpan<byte> ReadBinary(int length)
    {
        var start = Position + 1;
        if (length < 0 || start > _data.Length || length > _data.Length - start)
            throw FontErrors.Invalid("a charstring of the Type 1 font extends beyond the end of the font program.");

        Position = start + length;
        return _data.Slice(start, length);
    }

    /// <summary>Gets the text of a token: the characters of a name or of a number, the decoded bytes of a string.</summary>
    public readonly string GetText(PostScriptToken token)
    {
        var text = _data.Slice(token.Start, token.Length);
        return token.Kind switch
        {
            PostScriptTokenKind.String => DecodeString(text),
            PostScriptTokenKind.HexString => Encoding.Latin1.GetString(DecodeHex(text)),
            _ => Encoding.Latin1.GetString(text),
        };
    }

    /// <summary>Skips the rest of a value that starts with the token: the whole of an array, a procedure or a dictionary.</summary>
    public void SkipValue(PostScriptToken token)
    {
        if (token.Kind is not (PostScriptTokenKind.ArrayStart or PostScriptTokenKind.ProcedureStart or PostScriptTokenKind.DictionaryStart))
            return;

        var depth = 1;
        while (depth > 0)
        {
            var next = Read();
            switch (next.Kind)
            {
                case PostScriptTokenKind.EndOfData:
                    return;
                case PostScriptTokenKind.ArrayStart or PostScriptTokenKind.ProcedureStart or PostScriptTokenKind.DictionaryStart:
                    depth++;
                    break;
                case PostScriptTokenKind.ArrayEnd or PostScriptTokenKind.ProcedureEnd or PostScriptTokenKind.DictionaryEnd:
                    depth--;
                    break;
            }
        }
    }

    /// <summary>Reads the numbers of an array or a procedure that starts with the token; <see langword="null"/> when the value is not one.</summary>
    public double[]? ReadNumbers(PostScriptToken token, int maxCount)
    {
        if (token.Kind is not (PostScriptTokenKind.ArrayStart or PostScriptTokenKind.ProcedureStart))
            return null;

        var values = new List<double>();
        var isValid = true;
        while (true)
        {
            var next = Read();
            switch (next.Kind)
            {
                case PostScriptTokenKind.EndOfData or PostScriptTokenKind.ArrayEnd or PostScriptTokenKind.ProcedureEnd:
                    return isValid ? [.. values] : null;
                case PostScriptTokenKind.Number when values.Count < maxCount:
                    values.Add(next.Number);
                    break;
                default:
                    SkipValue(next);
                    isValid = false;
                    break;
            }
        }
    }

    private void SkipRegular()
    {
        while (Position < _data.Length && !IsWhiteSpace(_data[Position]) && !IsDelimiter(_data[Position]))
        {
            Position++;
        }
    }

    /// <summary>Skips a string: balanced parentheses, and escaped characters (PostScript Language Reference, 3.2.2).</summary>
    private void SkipString()
    {
        var depth = 1;
        while (Position < _data.Length)
        {
            var value = _data[Position++];
            switch (value)
            {
                case (byte)'\\':
                    Position++;
                    break;
                case (byte)'(':
                    depth++;
                    break;
                case (byte)')':
                    if (--depth == 0)
                        return;

                    break;
            }
        }

        Position = _data.Length;
    }

    private static string DecodeString(ReadOnlySpan<byte> text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var value = text[i];
            if (value != '\\' || i + 1 >= text.Length)
            {
                builder.Append((char)value);
                continue;
            }

            var escaped = text[++i];
            switch (escaped)
            {
                case (byte)'n':
                    builder.Append('\n');
                    break;
                case (byte)'r':
                    builder.Append('\r');
                    break;
                case (byte)'t':
                    builder.Append('\t');
                    break;
                case (byte)'b':
                    builder.Append('\b');
                    break;
                case (byte)'f':
                    builder.Append('\f');
                    break;
                case (byte)'\r':
                    // A backslash before an end of line continues the string on the next line
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    break;
                case (byte)'\n':
                    break;
                case >= (byte)'0' and <= (byte)'7':
                {
                    var code = escaped - '0';
                    for (var digits = 1; digits < 3 && i + 1 < text.Length && text[i + 1] is >= (byte)'0' and <= (byte)'7'; digits++)
                    {
                        code = (code * 8) + (text[++i] - '0');
                    }

                    builder.Append((char)(code & 0xFF));
                    break;
                }

                default:
                    builder.Append((char)escaped);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Decodes hexadecimal digits, ignoring white space; an odd final digit is followed by a zero.</summary>
    public static byte[] DecodeHex(ReadOnlySpan<byte> text)
    {
        var result = new byte[(text.Length + 1) / 2];
        var count = 0;
        var high = -1;
        foreach (var value in text)
        {
            var digit = HexDigit(value);
            if (digit < 0)
                continue;

            if (high < 0)
            {
                high = digit;
            }
            else
            {
                result[count++] = (byte)((high << 4) | digit);
                high = -1;
            }
        }

        if (high >= 0)
        {
            result[count++] = (byte)(high << 4);
        }

        return result.AsSpan(0, count).ToArray();
    }

    public static int HexDigit(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        _ => -1,
    };

    /// <summary>Parses a number: an integer, a real, or an integer with a radix (<c>16#FF</c>).</summary>
    private static bool TryParseNumber(ReadOnlySpan<byte> text, out double value)
    {
        value = 0;
        if (text.IsEmpty || text.Length > 64)
            return false;

        var first = text[0];
        if (first is not ((>= (byte)'0' and <= (byte)'9') or (byte)'+' or (byte)'-' or (byte)'.'))
            return false;

        var hash = text.IndexOf((byte)'#');
        if (hash > 0)
        {
            if (!int.TryParse(text[..hash], NumberStyles.None, CultureInfo.InvariantCulture, out var radix) || radix is < 2 or > 36 || hash == text.Length - 1)
                return false;

            long result = 0;
            foreach (var digitByte in text[(hash + 1)..])
            {
                var digit = digitByte switch
                {
                    >= (byte)'0' and <= (byte)'9' => digitByte - '0',
                    >= (byte)'a' and <= (byte)'z' => digitByte - 'a' + 10,
                    >= (byte)'A' and <= (byte)'Z' => digitByte - 'A' + 10,
                    _ => 99,
                };
                if (digit >= radix)
                    return false;

                result = (result * radix) + digit;
                if (result > uint.MaxValue)
                    return false;
            }

            // A number with a radix is the bits of an integer of 32 bits
            value = (int)(uint)result;
            return true;
        }

        if (!double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out value))
            return false;

        return double.IsFinite(value);
    }
}
