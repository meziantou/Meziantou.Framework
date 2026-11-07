namespace Meziantou.Framework.UrlPatternInternal;

/// <summary>The host parser of the URL Standard, which returns hosts in their serialized form.</summary>
/// <remarks>
/// <see href="https://url.spec.whatwg.org/#host-parsing">URL Standard - Host parsing</see>
/// </remarks>
internal static class HostParser
{
    // AllowUnassigned and UseStd3AsciiRules mirror the "domain to ASCII" parameters of the URL Standard,
    // which runs Unicode ToASCII with UseSTD3ASCIIRules and VerifyDnsLength both false
    private static readonly IdnMapping IdnMapping = new() { AllowUnassigned = true, UseStd3AsciiRules = false };

    /// <summary>Parses a host and returns its serialization.</summary>
    /// <param name="input">The host, as it appears in the URL.</param>
    /// <param name="isOpaque">Whether the URL does not have a special scheme, in which case a domain is kept as written.</param>
    /// <param name="host">The serialized host.</param>
    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#concept-host-parser">URL Standard - Host parser</see>
    /// </remarks>
    public static bool TryParse(string input, bool isOpaque, [NotNullWhen(true)] out string? host)
    {
        host = null;
        if (input.StartsWith('[', StringComparison.Ordinal))
        {
            if (input.Length < 2 || !input.EndsWith(']', StringComparison.Ordinal))
                return false;

            Span<ushort> address = stackalloc ushort[8];
            if (!TryParseIPv6(input.AsSpan(1, input.Length - 2), address))
                return false;

            host = SerializeIPv6(address);
            return true;
        }

        if (isOpaque)
            return TryParseOpaqueHost(input, out host);

        var domain = PercentEncoding.Decode(input);
        if (!TryDomainToAscii(domain, out var asciiDomain) || asciiDomain.Length == 0)
            return false;

        foreach (var c in asciiDomain)
        {
            if (IsForbiddenDomainCodePoint(c))
                return false;
        }

        if (EndsInANumber(asciiDomain))
            return TryParseIPv4(asciiDomain, out host);

        host = asciiDomain;
        return true;
    }

    /// <summary>Runs the "domain to ASCII" operation of the URL Standard, without its final checks.</summary>
    /// <remarks>
    /// <para>
    /// Unicode ToASCII is defined label by label, so an empty label (which a fixed-text part of a pattern such
    /// as "." consists of) has to stay empty instead of failing. The caller rejects an empty result and a
    /// forbidden domain code point, because a pattern and a URL do not reject them the same way.
    /// </para>
    /// <see href="https://url.spec.whatwg.org/#concept-domain-to-ascii">URL Standard - Domain to ASCII</see>
    /// </remarks>
    public static bool TryDomainToAscii(string domain, [NotNullWhen(true)] out string? result)
    {
        if (Ascii.IsValid(domain))
        {
            result = domain.ToLowerInvariant();
            return true;
        }

        var labels = domain.Split('.');
        for (var i = 0; i < labels.Length; i++)
        {
            // ToASCII maps a label to lower case before it encodes it. IdnMapping only does so when ICU is
            // available, so in globalization-invariant mode "CAFÉ" would encode as "xn--caf-pia" instead of
            // "xn--caf-dma". Mapping it here covers both modes
            var label = labels[i].ToLowerInvariant();
            if (Ascii.IsValid(label))
            {
                labels[i] = label;
                continue;
            }

            try
            {
                labels[i] = IdnMapping.GetAscii(label);
            }
            catch (ArgumentException)
            {
                result = null;
                return false;
            }
        }

        result = string.Join('.', labels);
        return true;
    }

    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#forbidden-domain-code-point">URL Standard - Forbidden domain code point</see>
    /// </remarks>
    public static bool IsForbiddenDomainCodePoint(char c)
    {
        // Forbidden host code points, plus the C0 controls, '%' and U+007F DELETE
        return c <= 0x20 || c is '%' or (char)0x7F || IsForbiddenHostCodePoint(c);
    }

    /// <summary>Serializes an IPv6 address the way the URL Standard does, brackets included.</summary>
    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#concept-ipv6-serializer">URL Standard - IPv6 serializer</see>
    /// </remarks>
    public static string SerializeIPv6(ReadOnlySpan<ushort> address)
    {
        // The longest run of zero pieces is replaced by "::", but only when it covers more than one piece
        var compress = -1;
        var compressLength = 1;
        for (var i = 0; i < address.Length; i++)
        {
            if (address[i] is not 0)
                continue;

            var length = 0;
            while (i + length < address.Length && address[i + length] is 0)
            {
                length++;
            }

            if (length > compressLength)
            {
                compress = i;
                compressLength = length;
            }

            i += length - 1;
        }

        var builder = new StringBuilder(41).Append('[');
        var ignoreZeroes = false;
        for (var i = 0; i < address.Length; i++)
        {
            if (ignoreZeroes)
            {
                if (address[i] is 0)
                    continue;

                ignoreZeroes = false;
            }

            if (i == compress)
            {
                builder.Append(i is 0 ? "::" : ":");
                ignoreZeroes = true;
                continue;
            }

            builder.Append(address[i].ToString("x", CultureInfo.InvariantCulture));
            if (i is not 7)
            {
                builder.Append(':');
            }
        }

        return builder.Append(']').ToString();
    }

    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#concept-ipv6-parser">URL Standard - IPv6 parser</see>
    /// </remarks>
    private static bool TryParseIPv6(ReadOnlySpan<char> input, Span<ushort> address)
    {
        address.Clear();
        var pieceIndex = 0;
        var compress = -1;
        var pointer = 0;

        if (At(input, pointer) is ':')
        {
            if (At(input, pointer + 1) is not ':')
                return false;

            pointer += 2;
            pieceIndex++;
            compress = pieceIndex;
        }

        while (pointer < input.Length)
        {
            if (pieceIndex == 8)
                return false;

            if (input[pointer] is ':')
            {
                if (compress != -1)
                    return false;

                pointer++;
                pieceIndex++;
                compress = pieceIndex;
                continue;
            }

            var value = 0;
            var length = 0;
            while (length < 4 && pointer < input.Length && char.IsAsciiHexDigit(input[pointer]))
            {
                value = (value * 0x10) + HexValue(input[pointer]);
                pointer++;
                length++;
            }

            if (At(input, pointer) is '.')
            {
                // The last 32 bits are written as an IPv4 address
                if (length == 0)
                    return false;

                pointer -= length;
                if (pieceIndex > 6)
                    return false;

                var numbersSeen = 0;
                while (pointer < input.Length)
                {
                    var ipv4Piece = -1;
                    if (numbersSeen > 0)
                    {
                        if (input[pointer] is '.' && numbersSeen < 4)
                        {
                            pointer++;
                        }
                        else
                        {
                            return false;
                        }
                    }

                    if (!char.IsAsciiDigit(At(input, pointer)))
                        return false;

                    while (pointer < input.Length && char.IsAsciiDigit(input[pointer]))
                    {
                        var number = input[pointer] - '0';
                        if (ipv4Piece == -1)
                        {
                            ipv4Piece = number;
                        }
                        else if (ipv4Piece == 0)
                        {
                            return false;
                        }
                        else
                        {
                            ipv4Piece = (ipv4Piece * 10) + number;
                        }

                        if (ipv4Piece > 255)
                            return false;

                        pointer++;
                    }

                    address[pieceIndex] = (ushort)((address[pieceIndex] * 0x100) + ipv4Piece);
                    numbersSeen++;
                    if (numbersSeen is 2 or 4)
                    {
                        pieceIndex++;
                    }
                }

                if (numbersSeen != 4)
                    return false;

                break;
            }

            if (At(input, pointer) is ':')
            {
                pointer++;
                if (pointer >= input.Length)
                    return false;
            }
            else if (pointer < input.Length)
            {
                return false;
            }

            address[pieceIndex] = (ushort)value;
            pieceIndex++;
        }

        if (compress != -1)
        {
            var swaps = pieceIndex - compress;
            pieceIndex = 7;
            while (pieceIndex != 0 && swaps > 0)
            {
                (address[pieceIndex], address[compress + swaps - 1]) = (address[compress + swaps - 1], address[pieceIndex]);
                pieceIndex--;
                swaps--;
            }
        }
        else if (pieceIndex != 8)
        {
            return false;
        }

        return true;

        static char At(ReadOnlySpan<char> input, int index) => index < input.Length ? input[index] : '\0';

        static int HexValue(char c) => char.IsAsciiDigit(c) ? c - '0' : (char.ToLowerInvariant(c) - 'a' + 10);
    }

    /// <summary>Determines whether a domain would be read as an IPv4 address.</summary>
    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#ends-in-a-number-checker">URL Standard - Ends in a number checker</see>
    /// </remarks>
    private static bool EndsInANumber(string input)
    {
        var parts = input.Split('.');
        var last = parts[^1];
        if (last.Length == 0)
        {
            if (parts.Length == 1)
                return false;

            last = parts[^2];
        }

        if (last.Length > 0 && !last.AsSpan().ContainsAnyExceptInRange('0', '9'))
            return true;

        return TryParseIPv4Number(last, out _);
    }

    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#concept-ipv4-parser">URL Standard - IPv4 parser</see>
    /// </remarks>
    private static bool TryParseIPv4(string input, [NotNullWhen(true)] out string? host)
    {
        host = null;

        var parts = input.Split('.');
        var count = parts[^1].Length == 0 && parts.Length > 1 ? parts.Length - 1 : parts.Length;
        if (count > 4)
            return false;

        Span<ulong> numbers = stackalloc ulong[4];
        for (var i = 0; i < count; i++)
        {
            if (!TryParseIPv4Number(parts[i], out numbers[i]))
                return false;
        }

        for (var i = 0; i < count - 1; i++)
        {
            if (numbers[i] > 255)
                return false;
        }

        // The last number fills every byte the other numbers left
        if (numbers[count - 1] >= 1UL << (8 * (5 - count)))
            return false;

        var ipv4 = numbers[count - 1];
        for (var i = 0; i < count - 1; i++)
        {
            ipv4 += numbers[i] << (8 * (3 - i));
        }

        host = string.Create(CultureInfo.InvariantCulture, $"{ipv4 >> 24}.{(ipv4 >> 16) & 0xFF}.{(ipv4 >> 8) & 0xFF}.{ipv4 & 0xFF}");
        return true;
    }

    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#ipv4-number-parser">URL Standard - IPv4 number parser</see>
    /// </remarks>
    private static bool TryParseIPv4Number(ReadOnlySpan<char> input, out ulong value)
    {
        // Any number that does not fit in 32 bits fails the IPv4 parser, so the value saturates there
        const ulong Saturation = 1UL << 32;

        value = 0;
        if (input.IsEmpty)
            return false;

        var radix = 10u;
        if (input.Length >= 2 && input[0] is '0' && input[1] is 'x' or 'X')
        {
            input = input[2..];
            radix = 16;
        }
        else if (input.Length >= 2 && input[0] is '0')
        {
            input = input[1..];
            radix = 8;
        }

        foreach (var c in input)
        {
            uint digit;
            if (char.IsAsciiDigit(c))
            {
                digit = (uint)(c - '0');
            }
            else if (radix == 16 && char.IsAsciiHexDigit(c))
            {
                digit = (uint)(char.ToLowerInvariant(c) - 'a' + 10);
            }
            else
            {
                return false;
            }

            if (digit >= radix)
                return false;

            value = Math.Min((value * radix) + digit, Saturation);
        }

        return true;
    }

    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#concept-opaque-host-parser">URL Standard - Opaque-host parser</see>
    /// </remarks>
    private static bool TryParseOpaqueHost(string input, [NotNullWhen(true)] out string? host)
    {
        foreach (var c in input)
        {
            if (IsForbiddenHostCodePoint(c))
            {
                host = null;
                return false;
            }
        }

        host = PercentEncoding.Encode(input, PercentEncodeSet.C0Control);
        return true;
    }

    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#forbidden-host-code-point">URL Standard - Forbidden host code point</see>
    /// </remarks>
    private static bool IsForbiddenHostCodePoint(char c)
    {
        return c is '\0' or '\t' or '\n' or '\r' or ' ' or '#' or '/' or ':' or '<' or '>' or '?' or '@' or '[' or '\\' or ']' or '^' or '|';
    }
}
