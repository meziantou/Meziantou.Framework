using System.Globalization;
using System.Numerics;
using System.Text;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Urls;

/// <summary>Host parsing and serializing: https://url.spec.whatwg.org/#host-parsing</summary>
internal static class HostParser
{
    private static readonly Uts46Options StrictOptions = new(CheckHyphens: true, CheckBidi: true, CheckJoiners: true, UseStd3AsciiRules: true, TransitionalProcessing: false, VerifyDnsLength: true, IgnoreInvalidPunycode: false);
    private static readonly Uts46Options NonStrictOptions = new(CheckHyphens: false, CheckBidi: true, CheckJoiners: true, UseStd3AsciiRules: false, TransitionalProcessing: false, VerifyDnsLength: false, IgnoreInvalidPunycode: false);
    private static readonly Uts46Options ToUnicodeOptions = new(CheckHyphens: false, CheckBidi: true, CheckJoiners: true, UseStd3AsciiRules: false, TransitionalProcessing: false, VerifyDnsLength: false, IgnoreInvalidPunycode: false);

    /// <summary>https://url.spec.whatwg.org/#forbidden-host-code-point</summary>
    public static bool IsForbiddenHostCodePoint(int c) => c is 0x00 or 0x09 or 0x0A or 0x0D or ' ' or '#' or '/' or ':' or '<' or '>' or '?' or '@' or '[' or '\\' or ']' or '^' or '|';

    /// <summary>https://url.spec.whatwg.org/#forbidden-domain-code-point</summary>
    public static bool IsForbiddenDomainCodePoint(int c) => IsForbiddenHostCodePoint(c) || c is <= 0x1F or '%' or 0x7F;

    /// <summary>https://url.spec.whatwg.org/#concept-host-parser. Returns null on failure.</summary>
    public static UrlHost? Parse(string input, bool isOpaque = false)
    {
        if (input.StartsWith('[', StringComparison.Ordinal))
        {
            if (!input.EndsWith(']', StringComparison.Ordinal))
                return null;

            var address = ParseIPv6(input.AsSpan(1, input.Length - 2));
            if (address is null)
                return null;

            return new UrlHost(UrlHostKind.IPv6, "[" + SerializeIPv6(address) + "]");
        }

        if (isOpaque)
            return ParseOpaqueHost(input);

        // The percent-decoding and the UTF-8 decoding of an ASCII string without '%' is the string itself
        var domain = !input.Contains('%', StringComparison.Ordinal) && Ascii.IsValid(input) ? input : WebEncodingRegistry.CreateDecoder(WebEncodingId.Utf8).DecodeToString(PercentEncoding.PercentDecode(input));
        var asciiDomain = ParseDomain(domain, beStrict: false);
        if (asciiDomain is null)
            return null;

        if (EndsInANumber(asciiDomain))
        {
            var ipv4 = ParseIPv4(asciiDomain);
            if (ipv4 is null)
                return null;

            return new UrlHost(UrlHostKind.IPv4, SerializeIPv4(ipv4.Value));
        }

        return new UrlHost(UrlHostKind.Domain, asciiDomain);
    }

    /// <summary>https://url.spec.whatwg.org/#concept-domain-parser. Returns null on failure.</summary>
    public static string? ParseDomain(string domain, bool beStrict)
    {
        if (beStrict)
            return Uts46.ToAscii(domain, StrictOptions);

        string? result;
        if (Ascii.IsValid(domain))
        {
            // Due to web compatibility, ASCII input is lowercased regardless of the outcome of Unicode ToASCII.
            result = Infra.InfraAscii.ToAsciiLowercase(domain);
        }
        else
        {
            result = Uts46.ToAscii(domain, NonStrictOptions);
            if (result is null)
                return null;
        }

        if (result.Length == 0)
            return null;

        foreach (var c in result)
        {
            if (IsForbiddenDomainCodePoint(c))
                return null;
        }

        return result;
    }

    /// <summary>https://url.spec.whatwg.org/#concept-domain-to-unicode</summary>
    public static string DomainToUnicode(string domain)
    {
        var result = Uts46.ToUnicode(domain, ToUnicodeOptions, out var hasError);
        return hasError ? domain : result;
    }

    /// <summary>https://url.spec.whatwg.org/#ends-in-a-number-checker</summary>
    private static bool EndsInANumber(string input)
    {
        // The last part, ignoring the empty part after a trailing dot
        if (input.Length == 0)
            return false;

        var value = input.AsSpan();
        if (value[^1] == '.')
        {
            value = value[..^1];
        }

        var last = value[(value.LastIndexOf('.') + 1)..];
        if (last.Length > 0 && last.IndexOfAnyExceptInRange('0', '9') < 0)
            return true;

        return ParseIPv4Number(last.ToString(), out _) is not null;
    }

    /// <summary>https://url.spec.whatwg.org/#concept-ipv4-parser. Returns null on failure.</summary>
    private static uint? ParseIPv4(string input)
    {
        var parts = input.Split('.').ToList();
        if (parts[^1].Length == 0 && parts.Count > 1)
        {
            parts.RemoveAt(parts.Count - 1);
        }

        if (parts.Count > 4)
            return null;

        var numbers = new List<BigInteger>(parts.Count);
        foreach (var part in parts)
        {
            var result = ParseIPv4Number(part, out _);
            if (result is null)
                return null;

            numbers.Add(result.Value);
        }

        for (var i = 0; i < numbers.Count - 1; i++)
        {
            if (numbers[i] > 255)
                return null;
        }

        if (numbers[^1] >= BigInteger.Pow(256, 5 - numbers.Count))
            return null;

        var ipv4 = numbers[^1];
        for (var i = 0; i < numbers.Count - 1; i++)
        {
            ipv4 += numbers[i] * BigInteger.Pow(256, 3 - i);
        }

        return (uint)ipv4;
    }

    /// <summary>https://url.spec.whatwg.org/#ipv4-number-parser. Returns null on failure.</summary>
    private static BigInteger? ParseIPv4Number(string input, out bool validationError)
    {
        validationError = false;
        if (input.Length == 0)
            return null;

        var radix = 10;
        var span = input.AsSpan();
        if (span.Length >= 2 && span[0] == '0' && span[1] is 'x' or 'X')
        {
            validationError = true;
            span = span[2..];
            radix = 16;
        }
        else if (span.Length >= 2 && span[0] == '0')
        {
            validationError = true;
            span = span[1..];
            radix = 8;
        }

        if (span.IsEmpty)
            return BigInteger.Zero;

        BigInteger result = 0;
        foreach (var c in span)
        {
            int digit;
            if (char.IsAsciiDigit(c))
            {
                digit = c - '0';
            }
            else if (c is >= 'a' and <= 'f')
            {
                digit = c - 'a' + 10;
            }
            else if (c is >= 'A' and <= 'F')
            {
                digit = c - 'A' + 10;
            }
            else
            {
                return null;
            }

            if (digit >= radix)
                return null;

            result = (result * radix) + digit;
        }

        return result;
    }

    /// <summary>https://url.spec.whatwg.org/#concept-ipv6-parser. Returns null on failure.</summary>
    private static ushort[]? ParseIPv6(ReadOnlySpan<char> input)
    {
        var address = new ushort[8];
        var pieceIndex = 0;
        int? compress = null;
        var pointer = 0;

        int C(ReadOnlySpan<char> s, int p) => p < s.Length ? s[p] : -1;

        if (C(input, pointer) == ':')
        {
            if (C(input, pointer + 1) != ':')
                return null;

            pointer += 2;
            pieceIndex++;
            compress = pieceIndex;
        }

        while (C(input, pointer) != -1)
        {
            if (pieceIndex == 8)
                return null;

            if (C(input, pointer) == ':')
            {
                if (compress is not null)
                    return null;

                pointer++;
                pieceIndex++;
                compress = pieceIndex;
                continue;
            }

            var value = 0;
            var length = 0;
            while (length < 4 && C(input, pointer) is var hex && hex != -1 && char.IsAsciiHexDigit((char)hex))
            {
                value = (value * 0x10) + HexValue((char)hex);
                pointer++;
                length++;
            }

            if (C(input, pointer) == '.')
            {
                if (length == 0)
                    return null;

                pointer -= length;
                if (pieceIndex > 6)
                    return null;

                var numbersSeen = 0;
                while (C(input, pointer) != -1)
                {
                    int? ipv4Piece = null;
                    if (numbersSeen > 0)
                    {
                        if (C(input, pointer) == '.' && numbersSeen < 4)
                        {
                            pointer++;
                        }
                        else
                        {
                            return null;
                        }
                    }

                    if (C(input, pointer) is var d && (d == -1 || !char.IsAsciiDigit((char)d)))
                        return null;

                    while (C(input, pointer) is var digit && digit != -1 && char.IsAsciiDigit((char)digit))
                    {
                        var number = digit - '0';
                        if (ipv4Piece is null)
                        {
                            ipv4Piece = number;
                        }
                        else if (ipv4Piece == 0)
                        {
                            return null;
                        }
                        else
                        {
                            ipv4Piece = (ipv4Piece * 10) + number;
                        }

                        if (ipv4Piece > 255)
                            return null;

                        pointer++;
                    }

                    address[pieceIndex] = (ushort)((address[pieceIndex] * 0x100) + ipv4Piece!.Value);
                    numbersSeen++;
                    if (numbersSeen is 2 or 4)
                    {
                        pieceIndex++;
                    }
                }

                if (numbersSeen != 4)
                    return null;

                break;
            }
            else if (C(input, pointer) == ':')
            {
                pointer++;
                if (C(input, pointer) == -1)
                    return null;
            }
            else if (C(input, pointer) != -1)
            {
                return null;
            }

            address[pieceIndex] = (ushort)value;
            pieceIndex++;
        }

        if (compress is not null)
        {
            var swaps = pieceIndex - compress.Value;
            pieceIndex = 7;
            while (pieceIndex != 0 && swaps > 0)
            {
                (address[pieceIndex], address[compress.Value + swaps - 1]) = (address[compress.Value + swaps - 1], address[pieceIndex]);
                pieceIndex--;
                swaps--;
            }
        }
        else if (pieceIndex != 8)
        {
            return null;
        }

        return address;

        static int HexValue(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => c - 'A' + 10,
        };
    }

    /// <summary>https://url.spec.whatwg.org/#concept-opaque-host-parser. Returns null on failure.</summary>
    private static UrlHost? ParseOpaqueHost(string input)
    {
        foreach (var rune in input.EnumerateRunes())
        {
            if (IsForbiddenHostCodePoint(rune.Value))
                return null;
        }

        var result = PercentEncoding.Utf8PercentEncode(input, PercentEncodeSet.C0Control);

        // The empty string is the empty host
        return result.Length == 0 ? UrlHost.EmptyHost : new UrlHost(UrlHostKind.Opaque, result);
    }

    /// <summary>https://url.spec.whatwg.org/#concept-ipv4-serializer</summary>
    private static string SerializeIPv4(uint address)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{address >> 24}.{(address >> 16) & 0xFF}.{(address >> 8) & 0xFF}.{address & 0xFF}");
    }

    /// <summary>https://url.spec.whatwg.org/#concept-ipv6-serializer</summary>
    private static string SerializeIPv6(ushort[] address)
    {
        var output = new StringBuilder();
        var compress = FindCompressedPieceIndex(address);
        var ignore0 = false;
        for (var pieceIndex = 0; pieceIndex < 8; pieceIndex++)
        {
            if (ignore0 && address[pieceIndex] == 0)
                continue;

            ignore0 = false;
            if (compress == pieceIndex)
            {
                output.Append(pieceIndex == 0 ? "::" : ":");
                ignore0 = true;
                continue;
            }

            output.Append(address[pieceIndex].ToString("x", CultureInfo.InvariantCulture));
            if (pieceIndex != 7)
            {
                output.Append(':');
            }
        }

        return output.ToString();
    }

    private static int? FindCompressedPieceIndex(ushort[] address)
    {
        int? longestIndex = null;
        var longestSize = 1;
        int? foundIndex = null;
        var foundSize = 0;
        for (var pieceIndex = 0; pieceIndex < 8; pieceIndex++)
        {
            if (address[pieceIndex] != 0)
            {
                if (foundSize > longestSize)
                {
                    longestIndex = foundIndex;
                    longestSize = foundSize;
                }

                foundIndex = null;
                foundSize = 0;
            }
            else
            {
                foundIndex ??= pieceIndex;
                foundSize++;
            }
        }

        if (foundSize > longestSize)
            return foundIndex;

        return longestIndex;
    }
}
