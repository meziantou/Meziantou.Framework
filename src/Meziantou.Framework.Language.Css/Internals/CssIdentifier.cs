using System.Text;

namespace Meziantou.Framework.Language.Css.Internals;

/// <summary>The character classes of the CSS tokenizer, and the escaping it undoes.</summary>
internal static class CssIdentifier
{
    public const char ReplacementCharacter = '�';

    public static bool IsNewline(char c) => c is '\n' or '\r' or '\f';

    public static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    public static bool IsHexDigit(char c) => char.IsAsciiHexDigit(c);

    /// <summary>What <see cref="CharAt"/> returns past the end of the text. It belongs to none of the classes that matter.</summary>
    public const char EndOfText = '\u0001';

    /// <summary>A letter, an underscore, or anything outside ASCII. A NUL counts, as the U+FFFD it is read as.</summary>
    public static bool IsIdentStart(char c) => char.IsAsciiLetter(c) || c is '_' or '\0' || c >= '\u0080';

    public static bool IsIdentChar(char c) => IsIdentStart(c) || char.IsAsciiDigit(c) || c == '-';

    /// <remarks>A NUL is not one: CSS reads it as U+FFFD.</remarks>
    public static bool IsNonPrintable(char c) => c is (>= '\u0001' and <= '\u0008') or '\u000B' or (>= '\u000E' and <= '\u001F') or '\u007F';

    /// <summary>Determines whether a backslash at <paramref name="index"/> starts an escape: it is not followed by a line break.</summary>
    /// <remarks>A backslash at the very end of the text is a valid escape too, which stands for U+FFFD.</remarks>
    public static bool IsValidEscape(string text, int index) => CharAt(text, index) == '\\' && !IsNewline(CharAt(text, index + 1));

    /// <summary>Determines whether the text at <paramref name="index"/> would start an ident sequence.</summary>
    public static bool WouldStartIdentSequence(string text, int index)
    {
        var first = CharAt(text, index);
        return first switch
        {
            '-' => IsIdentStart(CharAt(text, index + 1)) || CharAt(text, index + 1) == '-' || IsValidEscape(text, index + 1),
            '\\' => IsValidEscape(text, index),
            _ => IsIdentStart(first),
        };
    }

    /// <summary>Determines whether the text at <paramref name="index"/> would start a number.</summary>
    public static bool WouldStartNumber(string text, int index)
    {
        var first = CharAt(text, index);
        if (first is '+' or '-')
        {
            var second = CharAt(text, index + 1);
            return char.IsAsciiDigit(second) || (second == '.' && char.IsAsciiDigit(CharAt(text, index + 2)));
        }

        if (first == '.')
            return char.IsAsciiDigit(CharAt(text, index + 1));

        return char.IsAsciiDigit(first);
    }

    /// <summary>Reads an ident sequence starting at <paramref name="index"/>, resolving its escapes.</summary>
    /// <returns>The value, and in <paramref name="index"/> the position after it.</returns>
    public static string ConsumeIdentSequence(string text, ref int index)
    {
        // Most identifiers hold no escape, and are their own value.
        var start = index;
        while (index < text.Length && IsIdentChar(text[index]))
        {
            index++;
        }

        if (!IsValidEscape(text, index))
            return Normalize(text[start..index]);

        var builder = new StringBuilder();
        AppendNormalized(builder, text.AsSpan(start, index - start));
        while (true)
        {
            if (index < text.Length && IsIdentChar(text[index]))
            {
                AppendNormalized(builder, text[index]);
                index++;
            }
            else if (IsValidEscape(text, index))
            {
                index++;
                AppendCodePoint(builder, ConsumeEscapedCodePoint(text, ref index));
            }
            else
            {
                return builder.ToString();
            }
        }
    }

    /// <summary>Reads an escape whose backslash is just before <paramref name="index"/>, and returns the code point it stands for.</summary>
    public static int ConsumeEscapedCodePoint(string text, ref int index)
    {
        if (index >= text.Length)
            return ReplacementCharacter;

        if (IsHexDigit(text[index]))
        {
            var value = 0;
            var count = 0;
            while (count < 6 && index < text.Length && IsHexDigit(text[index]))
            {
                value = (value * 16) + HexValue(text[index]);
                index++;
                count++;
            }

            // One whitespace after a hexadecimal escape belongs to it, so that "\26 B" is "&B".
            if (index < text.Length && IsWhitespace(text[index]))
            {
                index += text[index] == '\r' && CharAt(text, index + 1) == '\n' ? 2 : 1;
            }

            return value is 0 or (>= 0xD800 and <= 0xDFFF) or > 0x10FFFF ? ReplacementCharacter : value;
        }

        if (char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(CharAt(text, index + 1)))
        {
            var codePoint = char.ConvertToUtf32(text[index], text[index + 1]);
            index += 2;
            return codePoint;
        }

        var c = text[index];
        index++;
        return c is '\0' || char.IsSurrogate(c) ? ReplacementCharacter : c;
    }

    public static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => c - 'A' + 10,
    };

    public static void AppendCodePoint(StringBuilder builder, int codePoint)
    {
        if (codePoint <= 0xFFFF)
        {
            builder.Append((char)codePoint);
        }
        else
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
        }
    }

    /// <summary>Replaces what the CSS preprocessing step replaces: a NUL and a lone surrogate become U+FFFD.</summary>
    public static string Normalize(string value)
    {
        if (!NeedsNormalization(value))
            return value;

        var builder = new StringBuilder(value.Length);
        AppendNormalized(builder, value);
        return builder.ToString();
    }

    public static void AppendNormalized(StringBuilder builder, ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }

            builder.Append(c is '\0' || char.IsSurrogate(c) ? ReplacementCharacter : c);
        }
    }

    public static void AppendNormalized(StringBuilder builder, char c) => builder.Append(c);

    private static bool NeedsNormalization(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\0')
                return true;

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(c))
                return true;
        }

        return false;
    }

    /// <summary>Reads the number at the start of a numeric token the way the tokenizer did, and returns where it ends.</summary>
    public static int ScanNumber(string text, out bool isInteger)
    {
        isInteger = true;
        var index = 0;
        if (index < text.Length && text[index] is '+' or '-')
        {
            index++;
        }

        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
        }

        if (index + 1 < text.Length && text[index] == '.' && char.IsAsciiDigit(text[index + 1]))
        {
            isInteger = false;
            index++;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index++;
            }
        }

        if (index < text.Length && text[index] is 'e' or 'E')
        {
            // A unit may start with an 'e' that is not an exponent, as in "1em".
            var next = index + 1;
            if (next < text.Length && text[next] is '+' or '-')
            {
                next++;
            }

            if (next < text.Length && char.IsAsciiDigit(text[next]))
            {
                isInteger = false;
                index = next;
                while (index < text.Length && char.IsAsciiDigit(text[index]))
                {
                    index++;
                }
            }
        }

        return index;
    }

    /// <summary>Gets the unit of a dimension token, with its escapes resolved.</summary>
    public static string GetDimensionUnit(string text)
    {
        var index = ScanNumber(text, out _);
        return ConsumeIdentSequence(text, ref index);
    }

    /// <summary>Lowercases the ASCII letters of <paramref name="value"/> only, which is how CSS compares keywords.</summary>
    public static string ToAsciiLowerCase(string value)
    {
        foreach (var c in value)
        {
            if (char.IsAsciiLetterUpper(c))
                return string.Create(value.Length, value, static (span, value) =>
                {
                    for (var i = 0; i < value.Length; i++)
                    {
                        span[i] = char.IsAsciiLetterUpper(value[i]) ? (char)(value[i] | 0x20) : value[i];
                    }
                });
        }

        return value;
    }

    /// <summary>Compares two names the way CSS compares keywords: ignoring the case of ASCII letters only.</summary>
    public static bool EqualsIgnoreAsciiCase(string? value, string expected)
    {
        if (value is null || value.Length != expected.Length)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c != expected[i] && (!char.IsAsciiLetter(c) || (c | 0x20) != (expected[i] | 0x20)))
                return false;
        }

        return true;
    }

    /// <summary>Determines whether <paramref name="name"/> reads back as itself when written as an identifier.</summary>
    public static bool IsValidIdentifier(string name)
    {
        if (name.Length == 0 || !WouldStartIdentSequence(name, 0))
            return false;

        foreach (var c in name)
        {
            if (!IsIdentChar(c) || c is '\0' || char.IsSurrogate(c))
                return false;
        }

        return true;
    }

    /// <summary>Writes <paramref name="value"/> as an identifier, escaping what has to be, as CSSOM serializes one.</summary>
    public static string SerializeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\0')
            {
                builder.Append(ReplacementCharacter);
            }
            else if (c is (>= '\u0001' and <= '\u001F') or '\u007F' || (i == 0 && char.IsAsciiDigit(c)) || (i == 1 && char.IsAsciiDigit(c) && value[0] == '-'))
            {
                builder.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
            }
            else if (i == 0 && c == '-' && value.Length == 1)
            {
                builder.Append('\\').Append(c);
            }
            else if (c >= '\u0080' || c is '-' or '_' || char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('\\').Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Writes <paramref name="value"/> as a string between <paramref name="quote"/> characters, as CSSOM serializes one.</summary>
    public static string SerializeString(string value, char quote)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append(quote);
        foreach (var c in value)
        {
            if (c == '\0')
            {
                builder.Append(ReplacementCharacter);
            }
            else if (c is (>= '\u0001' and <= '\u001F') or '\u007F')
            {
                builder.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
            }
            else if (c == quote || c == '\\')
            {
                builder.Append('\\').Append(c);
            }
            else
            {
                builder.Append(c);
            }
        }

        builder.Append(quote);
        return builder.ToString();
    }

    public static char CharAt(string text, int index) => (uint)index < (uint)text.Length ? text[index] : EndOfText;
}
