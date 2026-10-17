using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Encodings;

namespace Meziantou.Framework.WebPlatform.Urls;

/// <summary>Percent-encode sets: https://url.spec.whatwg.org/#percent-encoded-bytes</summary>
internal enum PercentEncodeSet
{
    C0Control,
    Fragment,
    Query,
    SpecialQuery,
    Path,
    Userinfo,
    Component,
    FormUrlEncoded,
}

internal static class PercentEncoding
{
    private static ReadOnlySpan<byte> HexDigits => "0123456789ABCDEF"u8;

    /// <summary>Returns true when the code point is in the percent-encode set.</summary>
    public static bool IsInSet(int codePoint, PercentEncodeSet set)
    {
        // C0 control percent-encode set: C0 controls and all code points greater than U+007E (~)
        if (codePoint is <= 0x1F or > 0x7E)
            return true;

        return set switch
        {
            PercentEncodeSet.C0Control => false,
            PercentEncodeSet.Fragment => codePoint is ' ' or '"' or '<' or '>' or '`',
            PercentEncodeSet.Query => IsInQuerySet(codePoint),
            PercentEncodeSet.SpecialQuery => IsInQuerySet(codePoint) || codePoint == '\'',
            PercentEncodeSet.Path => IsInPathSet(codePoint),
            PercentEncodeSet.Userinfo => IsInUserinfoSet(codePoint),
            PercentEncodeSet.Component => IsInComponentSet(codePoint),
            PercentEncodeSet.FormUrlEncoded => IsInComponentSet(codePoint) || codePoint is '!' or (>= '\'' and <= ')') or '~',
            _ => throw new ArgumentOutOfRangeException(nameof(set)),
        };

        static bool IsInQuerySet(int c) => c is ' ' or '"' or '#' or '<' or '>';
        static bool IsInPathSet(int c) => IsInQuerySet(c) || c is '?' or '^' or '`' or '{' or '}';
        static bool IsInUserinfoSet(int c) => IsInPathSet(c) || c is '/' or ':' or ';' or '=' or '@' or (>= '[' and <= ']') or '|';
        static bool IsInComponentSet(int c) => IsInUserinfoSet(c) || c is (>= '$' and <= '&') or '+' or ',';
    }

    /// <summary>https://url.spec.whatwg.org/#percent-encode</summary>
    public static void AppendPercentEncodedByte(StringBuilder output, byte value)
    {
        output.Append('%').Append((char)HexDigits[value >> 4]).Append((char)HexDigits[value & 0xF]);
    }

    /// <summary>https://url.spec.whatwg.org/#utf-8-percent-encode (for a single code point)</summary>
    public static void AppendUtf8PercentEncoded(StringBuilder output, int codePoint, PercentEncodeSet set)
    {
        if (!IsInSet(codePoint, set))
        {
            output.Append((char)codePoint);
            return;
        }

        Span<byte> buffer = stackalloc byte[4];
        var length = new Rune(codePoint).EncodeToUtf8(buffer);
        foreach (var value in buffer[..length])
        {
            AppendPercentEncodedByte(output, value);
        }
    }

    /// <summary>https://url.spec.whatwg.org/#string-utf-8-percent-encode</summary>
    public static string Utf8PercentEncode(string input, PercentEncodeSet set)
    {
        var output = new StringBuilder(input.Length);
        foreach (var rune in input.EnumerateRunes())
        {
            AppendUtf8PercentEncoded(output, rune.Value, set);
        }

        return output.ToString();
    }

    /// <summary>https://url.spec.whatwg.org/#string-percent-encode-after-encoding</summary>
    public static void AppendPercentEncodeAfterEncoding(StringBuilder output, WebEncodingId encoding, ReadOnlySpan<char> input, PercentEncodeSet set)
    {
        var spaceAsPlus = set == PercentEncodeSet.FormUrlEncoded;
        if (encoding == WebEncodingId.Utf8)
        {
            foreach (var rune in input.EnumerateRunes())
            {
                if (spaceAsPlus && rune.Value == ' ')
                {
                    output.Append('+');
                    continue;
                }

                AppendUtf8PercentEncoded(output, rune.Value, set);
            }

            return;
        }

        var encoder = WebEncodingRegistry.CreateEncoder(encoding);
        Span<byte> buffer = stackalloc byte[WebEncoder.MaxBytesPerCodePoint];
        foreach (var rune in input.EnumerateRunes())
        {
            var success = encoder.Encode(rune.Value, buffer, out var written, out var errorCodePoint);
            AppendBytes(output, buffer[..written], set, spaceAsPlus);
            if (!success)
            {
                output.Append("%26%23").Append(errorCodePoint.ToString(CultureInfo.InvariantCulture)).Append("%3B");
            }
        }

        var flushed = encoder.Flush(buffer);
        AppendBytes(output, buffer[..flushed], set, spaceAsPlus);

        static void AppendBytes(StringBuilder output, ReadOnlySpan<byte> bytes, PercentEncodeSet set, bool spaceAsPlus)
        {
            foreach (var value in bytes)
            {
                if (spaceAsPlus && value == 0x20)
                {
                    output.Append('+');
                    continue;
                }

                if (!IsInSet(value, set))
                {
                    output.Append((char)value);
                }
                else
                {
                    AppendPercentEncodedByte(output, value);
                }
            }
        }
    }

    /// <summary>https://url.spec.whatwg.org/#percent-decode</summary>
    public static byte[] PercentDecode(ReadOnlySpan<byte> input)
    {
        var output = new List<byte>(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            var value = input[i];
            if (value == '%' && i + 2 < input.Length && char.IsAsciiHexDigit((char)input[i + 1]) && char.IsAsciiHexDigit((char)input[i + 2]))
            {
                output.Add((byte)((HexValue(input[i + 1]) << 4) | HexValue(input[i + 2])));
                i += 2;
                continue;
            }

            output.Add(value);
        }

        return [.. output];

        static int HexValue(byte c) => c switch
        {
            >= (byte)'0' and <= (byte)'9' => c - '0',
            >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
            _ => c - 'A' + 10,
        };
    }

    /// <summary>https://url.spec.whatwg.org/#string-percent-decode</summary>
    public static byte[] PercentDecode(string input) => PercentDecode(Encoding.UTF8.GetBytes(input));
}
