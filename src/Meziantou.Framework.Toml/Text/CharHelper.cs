using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Meziantou.Framework.Toml.Text;

internal static partial class CharHelper
{
    public static readonly Func<Char32, bool> IsHexFunc = IsHex;
    public static readonly Func<Char32, bool> IsOctalFunc = IsOctal;
    public static readonly Func<Char32, bool> IsBinaryFunc = IsBinary;

    public static readonly Func<Char32, int> HexToDecFunc = HexToDecimal;
    public static readonly Func<Char32, int> OctalToDecFunc = OctalToDecimal;
    public static readonly Func<Char32, int> BinaryToDecFunc = BinaryToDecimal;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsControlCharacter(Char32 c)
    {
        // The end-of-file sentinel is negative, and is not a character
        return (c >= 0 && c <= 0x1F) || c == 0x7F;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKeyStart(Char32 c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c == '-' || c >= '0' && c <= '9';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKeyContinue(Char32 c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c == '-' || c >= '0' && c <= '9';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentifierStart(Char32 c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentifierContinue(Char32 c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValidUnicodeScalarValue(Char32 c)
    {
        return c >= 0 && c <= 0xD7FF || c >= 0xE000 && c <= 0x10FFFF;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigit(Char32 c)
    {
        return (c >= '0' && c <= '9');
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDateTime(Char32 c)
    {
        return IsDigit(c) || c == ':' || c == '-' || c == 'Z' || c == 'T' || c == 'z' || c == 't' || c == '+' || c == '.';
    }

    // The generic IndexOfAnyInRange boxes its bounds before the method is optimized, which a short-lived process, such as one
    // that loads its configuration once, never gets past
    private static readonly SearchValues<char> Surrogates = CreateSurrogates();

    private static SearchValues<char> CreateSurrogates()
    {
        Span<char> surrogates = stackalloc char['\uDFFF' - '\uD800' + 1];
        for (var i = 0; i < surrogates.Length; i++)
        {
            surrogates[i] = (char)('\uD800' + i);
        }

        return SearchValues.Create(surrogates);
    }

    /// <summary>
    /// Gets the index of the first surrogate that is not part of a pair, which no TOML string or key can hold, or -1.
    /// </summary>
    public static int IndexOfUnpairedSurrogate(ReadOnlySpan<char> text)
    {
        var start = text.IndexOfAny(Surrogates);
        if (start < 0)
        {
            return -1;
        }

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                return i;
            }
        }

        return -1;
    }

    public static string GetUnpairedSurrogateMessage(string text, int index, string kind)
        => $"The {kind} contains an unpaired surrogate (U+{(int)text[index]:X4}) at index {index}, which TOML cannot represent.";

    /// <summary>
    /// Escape a C# string to a TOML string
    /// </summary>
    public static string EscapeForToml(this string text, bool allowNewLinesAndSpace = false)
    {
        StringBuilder? builder = null;
        for (int i = 0; i < text.Length; i++)
        {
            var start = i;
            var c = text[i];

            string? str = null;

            // Handle special characters for when multiline/spaces are allowed
            if (allowNewLinesAndSpace)
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    str = "\r\n";
                    i++;
                    c = '\n';
                }
                else if (c == '\n')
                {
                    str = "\n";
                }
                else if (c == '\t')
                {
                    str = "\t";
                }
            }

            str ??= EscapeChar(text[i]);

            if (str is not null)
            {
                if (builder == null)
                {
                    // A \r\n pair moves i past the \r, which must not be copied twice
                    builder = new StringBuilder(text.Length * 2);
                    builder.Append(text.AsSpan(0, start));
                }
                builder.Append(str);
            }
            else
            {
                builder?.Append(c);
            }
        }
        return builder?.ToString() ?? text;
    }

    private static string? EscapeChar(char c)
    {
        if (c < ' ' || c == '"' || c == '\\' || char.IsControl(c))
        {
            switch (c)
            {
                case '\b':
                    return @"\b";
                case '\t':
                    return @"\t";
                case '\n':
                    return @"\n";
                case '\f':
                    return @"\f";
                case '\r':
                    return @"\r";
                case '"':
                    return @"\""";
                case '\\':
                    return @"\\";
                default:
                    return $"\\u{(ushort)c:X4}";
            }
        }
        return null;
    }

    /// <summary>
    /// Converts a string that may have control characters to a printable string
    /// </summary>
    public static string? ToPrintableString(this string? text)
    {
        if (text is null) return null;
        StringBuilder? builder = null;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var str = text[i].ToPrintableString();
            if (str is not null)
            {
                if (builder == null)
                {
                    builder = new StringBuilder(text.Length * 2);
                    builder.Append(text.AsSpan(0, i));
                }
                builder.Append(str);
            }
            else
            {
                builder?.Append(c);
            }
        }
        return builder?.ToString() ?? text;
    }

    public static string? ToPrintableString(this char c)
    {
        if (c < ' ' || c == '\u007F' || IsWhiteSpace(c))
        {
            switch (c)
            {
                case ' ':
                    return @" ";
                case '\b':
                    return @"\b";
                case '\r':
                    return @"␍";
                case '\n':
                    return @"␤";
                case '\t':
                    return @"\t";
                case '\a':
                    return @"\a";
                case '\v':
                    return @"\v";
                case '\f':
                    return @"\f";
                default:
                    return $"\\u{(int)c:X4}";
            }
        }
        return null;
    }

    public static bool IsWhiteSpace(Char32 c)
    {
        return c == ' ' || // space
               c == '\t'; // horizontal tab
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWhiteSpaceOrNewLine(Char32 c)
    {
        return c == ' ' || // space
               c == '\t' || // horizontal tab
               c == '\r' || // \r
               c == '\n'; // \n
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNewLine(Char32 c)
    {
        return c == '\r' || // \r
               c == '\n'; // \n
    }

    public static int HexToDecimal(Char32 c)
    {
        Debug.Assert(IsHex(c));
        return (c >= '0' && c <= '9') ? c - '0' : (c >= 'a' && c <= 'f') ? (c - 'a') + 10 : (c - 'A') + 10;
    }

    public static int OctalToDecimal(Char32 c)
    {
        Debug.Assert(IsOctal(c));
        return c - '0';
    }

    public static int BinaryToDecimal(Char32 c)
    {
        Debug.Assert(IsBinary(c));
        return c - '0';
    }

    private static bool IsHex(Char32 c)
    {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
    private static bool IsOctal(Char32 c)
    {
        return (c >= '0' && c <= '7');
    }
    private static bool IsBinary(Char32 c)
    {
        return (c == '0' || c == '1');
    }

    public static void AppendUtf32(this StringBuilder builder, Char32 utf32)
    {
        if (utf32 < 65536)
        {
            builder.Append((char) utf32);
            return;
        }
        utf32 -= 65536;
        builder.Append((char) (utf32 / 1024 + 55296));
        builder.Append((char) (utf32 % 1024 + 56320));
    }
}
