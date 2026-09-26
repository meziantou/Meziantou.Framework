using System;
using System.Buffers;
using System.Text;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Parsing;

internal static class TomlStringDecoder
{
    public static string Decode(ReadOnlySpan<char> rawTokenText, TokenKind tokenKind)
    {
        return tokenKind switch
        {
            TokenKind.String => DecodeBasic(rawTokenText, isMultiline: false),
            TokenKind.StringMulti => DecodeBasic(rawTokenText, isMultiline: true),
            TokenKind.StringLiteral => DecodeLiteral(rawTokenText, isMultiline: false),
            TokenKind.StringLiteralMulti => DecodeLiteral(rawTokenText, isMultiline: true),
            _ => throw new ArgumentOutOfRangeException(nameof(tokenKind), tokenKind, "Unsupported string token kind."),
        };
    }

    private static string DecodeLiteral(ReadOnlySpan<char> rawTokenText, bool isMultiline)
    {
        var delimiterLength = isMultiline ? 3 : 1;
        if (rawTokenText.Length < delimiterLength * 2)
        {
            return string.Empty;
        }

        var content = rawTokenText.Slice(delimiterLength, rawTokenText.Length - (delimiterLength * 2));
        if (isMultiline)
        {
            content = SkipImmediateNewLine(content);
        }

        return content.ToString();
    }

    private static string DecodeBasic(ReadOnlySpan<char> rawTokenText, bool isMultiline)
    {
        var delimiterLength = isMultiline ? 3 : 1;
        if (rawTokenText.Length < delimiterLength * 2)
        {
            return string.Empty;
        }

        var content = rawTokenText.Slice(delimiterLength, rawTokenText.Length - (delimiterLength * 2));
        if (isMultiline)
        {
            content = SkipImmediateNewLine(content);
        }

        if (content.IndexOf('\\') < 0)
        {
            return content.ToString();
        }

        // Upper bound: escape sequences always reduce (or keep) the number of UTF-16 code units compared to the raw text.
        // Rent a buffer once and write the decoded output directly.
        var rented = ArrayPool<char>.Shared.Rent(content.Length);
        try
        {
            var written = 0;
            for (var index = 0; index < content.Length; index++)
            {
                var c = content[index];
                if (c != '\\')
                {
                    rented[written++] = c;
                    continue;
                }

                if (index + 1 >= content.Length)
                {
                    throw new FormatException("Invalid escape sequence at end of string.");
                }

                var escape = content[++index];
                switch (escape)
                {
                    case 'b':
                        rented[written++] = '\b';
                        break;
                    case 't':
                        rented[written++] = '\t';
                        break;
                    case 'n':
                        rented[written++] = '\n';
                        break;
                    case 'f':
                        rented[written++] = '\f';
                        break;
                    case 'r':
                        rented[written++] = '\r';
                        break;
                    case '"':
                        rented[written++] = '"';
                        break;
                    case '\\':
                        rented[written++] = '\\';
                        break;
                    case 'e':
                        rented[written++] = '\u001B';
                        break;
                    case 'x':
                        rented[written++] = (char)ReadHexScalar(content, ref index, digits: 2);
                        break;
                    case 'u':
                        AppendUnicodeScalar(rented, ref written, ReadHexScalar(content, ref index, digits: 4));
                        break;
                    case 'U':
                        AppendUnicodeScalar(rented, ref written, ReadHexScalar(content, ref index, digits: 8));
                        break;
                    case ' ':
                    case '\t':
                    case '\r':
                    case '\n':
                        SkipEscapedWhitespaceAndNewlines(content, ref index);
                        break;
                    default:
                        throw new FormatException($"Unexpected escape character `{escape}` in string.");
                }
            }

            return new string(rented, 0, written);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static ReadOnlySpan<char> SkipImmediateNewLine(ReadOnlySpan<char> content)
    {
        if (content.Length == 0)
        {
            return content;
        }

        if (content[0] == '\r')
        {
            if (content.Length > 1 && content[1] == '\n')
            {
                return content.Slice(2);
            }

            return content.Slice(1);
        }

        return content[0] == '\n' ? content.Slice(1) : content;
    }

    private static int ReadHexScalar(ReadOnlySpan<char> content, ref int index, int digits)
    {
        if (index + digits >= content.Length)
        {
            throw new FormatException("Invalid hexadecimal escape sequence.");
        }

        var value = 0;
        for (var i = 0; i < digits; i++)
        {
            var c = content[index + 1 + i];
            if (!CharHelper.IsHexFunc(c))
            {
                throw new FormatException("Invalid hexadecimal escape sequence.");
            }

            value = (value << 4) + CharHelper.HexToDecimal(c);
        }

        index += digits;
        return value;
    }

    private static void AppendUnicodeScalar(char[] buffer, ref int written, int value)
    {
        if (!CharHelper.IsValidUnicodeScalarValue(value))
        {
            throw new FormatException($"Invalid Unicode scalar value [{value:X}].");
        }

        if ((uint)value <= 0xFFFFu)
        {
            buffer[written++] = (char)value;
            return;
        }

        value -= 0x10000;
        buffer[written++] = (char)(0xD800 | (value >> 10));
        buffer[written++] = (char)(0xDC00 | (value & 0x3FF));
    }

    private static void SkipEscapedWhitespaceAndNewlines(ReadOnlySpan<char> content, ref int index)
    {
        var position = index;
        var skippedNewLine = false;
        while (position < content.Length)
        {
            var c = content[position];
            if (c == ' ' || c == '\t')
            {
                position++;
                continue;
            }

            if (c == '\n')
            {
                skippedNewLine = true;
                position++;
                continue;
            }

            if (c == '\r')
            {
                skippedNewLine = true;
                position++;
                if (position < content.Length && content[position] == '\n')
                {
                    position++;
                }

                continue;
            }

            break;
        }

        if (!skippedNewLine)
        {
            throw new FormatException("Invalid escape `\\`. It must skip at least one line.");
        }

        index = Math.Max(index, position - 1);
    }
}
