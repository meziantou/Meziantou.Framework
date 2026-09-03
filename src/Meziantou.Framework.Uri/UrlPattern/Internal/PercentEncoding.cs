using System.Buffers;

namespace Meziantou.Framework.UrlPatternInternal;

/// <summary>UTF-8 percent-encoding and percent-decoding, as defined by the URL Standard.</summary>
/// <remarks>
/// <see href="https://url.spec.whatwg.org/#percent-encoded-bytes">URL Standard - Percent-encoded bytes</see>
/// </remarks>
internal static class PercentEncoding
{
    private const string HexDigits = "0123456789ABCDEF";

    /// <summary>Percent-encodes the code points of <paramref name="value"/> that belong to <paramref name="set"/>.</summary>
    /// <remarks>
    /// U+0025 (%) belongs to none of the sets, so an escape sequence that is already present is left alone
    /// instead of being doubly encoded.
    /// <see href="https://url.spec.whatwg.org/#string-utf-8-percent-encode">URL Standard - UTF-8 percent-encode</see>
    /// </remarks>
    public static string Encode(string value, PercentEncodeSet set)
    {
        var index = IndexOfFirstEncodedCodePoint(value, set);
        if (index < 0)
            return value;

        var builder = new StringBuilder(value.Length + 8);
        builder.Append(value, 0, index);
        AppendEncoded(builder, value.AsSpan(index), set);

        return builder.ToString();
    }

    /// <summary>Percent-encodes a single code point, appending the result to <paramref name="builder"/>.</summary>
    public static void EncodeCodePoint(StringBuilder builder, Rune codePoint, PercentEncodeSet set)
    {
        if (codePoint.IsAscii && !ShouldEncode((char)codePoint.Value, set))
        {
            builder.Append((char)codePoint.Value);
            return;
        }

        Span<byte> utf8 = stackalloc byte[4];
        var length = codePoint.EncodeToUtf8(utf8);
        for (var i = 0; i < length; i++)
        {
            var b = utf8[i];
            builder.Append('%');
            builder.Append(HexDigits[b >> 4]);
            builder.Append(HexDigits[b & 0xF]);
        }
    }

    /// <summary>Percent-decodes every valid escape sequence in <paramref name="value"/>, interpreting the bytes as UTF-8.</summary>
    /// <remarks>
    /// A '%' that is not followed by two hexadecimal digits is kept as-is, as the URL Standard requires.
    /// <see href="https://url.spec.whatwg.org/#percent-decode">URL Standard - Percent-decode</see>
    /// </remarks>
    public static string Decode(string value)
    {
        if (!value.Contains('%', StringComparison.Ordinal))
            return value;

        var bytes = new byte[Encoding.UTF8.GetMaxByteCount(value.Length)];
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '%' && i + 2 < value.Length && char.IsAsciiHexDigit(value[i + 1]) && char.IsAsciiHexDigit(value[i + 2]))
            {
                bytes[count++] = (byte)((GetHexValue(value[i + 1]) << 4) | GetHexValue(value[i + 2]));
                i += 2;
                continue;
            }

            // The value is a .NET string, so a non-ASCII code point has to be re-encoded to UTF-8 bytes
            if (char.IsAscii(c))
            {
                bytes[count++] = (byte)c;
                continue;
            }

            var charCount = char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]) ? 2 : 1;
            count += Encoding.UTF8.GetBytes(value.AsSpan(i, charCount), bytes.AsSpan(count));
            i += charCount - 1;
        }

        return Encoding.UTF8.GetString(bytes.AsSpan(0, count));
    }

    private static void AppendEncoded(StringBuilder builder, ReadOnlySpan<char> value, PercentEncodeSet set)
    {
        // A lone surrogate is replaced by U+FFFD, which is how a DOMString becomes a USVString
        while (!value.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(value, out var rune, out var consumed) is not OperationStatus.Done)
            {
                rune = Rune.ReplacementChar;
            }

            EncodeCodePoint(builder, rune, set);
            value = value[consumed..];
        }
    }

    private static int IndexOfFirstEncodedCodePoint(string value, PercentEncodeSet set)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!char.IsAscii(c) || ShouldEncode(c, set))
                return i;
        }

        return -1;
    }

    private static int GetHexValue(char c) => char.IsAsciiDigit(c) ? c - '0' : (char.ToLowerInvariant(c) - 'a' + 10);

    /// <summary>Determines whether an ASCII code point belongs to the specified percent-encode set.</summary>
    private static bool ShouldEncode(char c, PercentEncodeSet set)
    {
        // C0 control percent-encode set: C0 controls and all code points greater than U+007E (~)
        if (c <= 0x1F || c > '~')
            return true;

        if (set is PercentEncodeSet.C0Control)
            return false;

        // Shared by the fragment set and the query set, which the remaining sets extend
        if (c is ' ' or '"' or '<' or '>')
            return true;

        // The fragment set is the only one that covers '`' but not '#'
        if (set is PercentEncodeSet.Fragment)
            return c is '`';

        if (c is '#')
            return true;

        if (set is PercentEncodeSet.Query)
            return false;

        if (set is PercentEncodeSet.SpecialQuery)
            return c is '\'';

        if (c is '?' or '`' or '{' or '}')
            return true;

        if (set is PercentEncodeSet.Path)
            return false;

        return c is '/' or ':' or ';' or '=' or '@' or '[' or '\\' or ']' or '^' or '|';
    }
}
