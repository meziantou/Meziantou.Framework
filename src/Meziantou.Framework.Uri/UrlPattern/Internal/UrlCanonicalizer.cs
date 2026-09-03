using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.UrlPatternInternal;

/// <summary>Rewrites a component value into the form a parsed URL would have.</summary>
/// <remarks>
/// <para>
/// Every component of a pattern is canonicalized when the pattern is compiled, one fixed-text part at a
/// time, and the values matched against it are canonicalized the same way. The two are only comparable
/// because both sides go through these methods.
/// </para>
/// <para>
/// The spec defines each of these in terms of the URL Standard's basic URL parser applied to a dummy
/// "https://dummy.invalid/" record with a state override. Only the states that a component can reach are
/// implemented here, which is why the dummy record does not appear.
/// </para>
/// <see href="https://urlpattern.spec.whatwg.org/#canon-encoding-callbacks">WHATWG URL Pattern Spec - Encoding callbacks</see>
/// </remarks>
internal static class UrlCanonicalizer
{
    // AllowUnassigned and UseStd3AsciiRules mirror the "domain to ASCII" parameters of the URL Standard,
    // which runs Unicode ToASCII with UseSTD3ASCIIRules and VerifyDnsLength both false
    private static readonly IdnMapping IdnMapping = new() { AllowUnassigned = true, UseStd3AsciiRules = false };
    private static readonly SearchValues<char> TabsAndNewlines = SearchValues.Create("\t\n\r");

    /// <summary>Canonicalizes a protocol.</summary>
    /// <remarks>
    /// The spec parses "&lt;value&gt;://dummy.invalid/" and returns the scheme of the result, which accepts
    /// exactly the scheme grammar and lowercases it.
    /// <see href="https://urlpattern.spec.whatwg.org/#canon-a-protocol">WHATWG URL Pattern Spec - Canonicalize a protocol</see>
    /// </remarks>
    public static string CanonicalizeProtocol(string value)
    {
        if (value.Length == 0)
            return value;

        // The basic URL parser is entered without a URL record, so it also trims the leading C0 controls and spaces
        value = TrimLeadingC0ControlsAndSpaces(RemoveTabsAndNewlines(value));
        if (value.Length == 0)
            throw new UrlPatternException("A protocol cannot be empty");

        if (!char.IsAsciiLetter(value[0]))
            throw new UrlPatternException($"Invalid protocol: '{value}' must start with an ASCII letter");

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
                throw new UrlPatternException($"Invalid protocol: '{value}' contains '{c}'");
        }

        return value.ToLowerInvariant();
    }

    /// <summary>Canonicalizes a username.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#canon-a-username">WHATWG URL Pattern Spec - Canonicalize a username</see>
    /// </remarks>
    public static string CanonicalizeUsername(string value)
    {
        // Setting the username of a URL record percent-encodes it, without going through the parser,
        // so unlike the other components a tab or a newline is encoded rather than removed
        return PercentEncoding.Encode(value, PercentEncodeSet.UserInfo);
    }

    /// <summary>Canonicalizes a password.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#canon-a-password">WHATWG URL Pattern Spec - Canonicalize a password</see>
    /// </remarks>
    public static string CanonicalizePassword(string value)
    {
        return PercentEncoding.Encode(value, PercentEncodeSet.UserInfo);
    }

    /// <summary>Canonicalizes a hostname.</summary>
    /// <remarks>
    /// <para>
    /// The IPv4 normalization of the URL Standard (which rewrites "0x7f.1" as "127.0.0.1") is deliberately
    /// not applied: the values a pattern is matched against come from a <see cref="Uri"/>, which does not
    /// apply it either, so normalizing only the pattern would stop the two from lining up.
    /// </para>
    /// <see href="https://urlpattern.spec.whatwg.org/#canon-a-hostname">WHATWG URL Pattern Spec - Canonicalize a hostname</see>
    /// </remarks>
    public static string CanonicalizeHostname(string value)
    {
        if (value.Length == 0)
            return value;

        value = RemoveTabsAndNewlines(value);
        if (value.Length == 0)
            return value;

        // Hostname state stops at whatever would start the next component, and rejects a port
        var insideBrackets = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '[')
            {
                insideBrackets = true;
            }
            else if (c is ']')
            {
                insideBrackets = false;
            }
            else if (c is ':' && !insideBrackets)
            {
                throw new UrlPatternException($"Invalid hostname: '{value}' contains a port");
            }
            else if (c is '/' or '?' or '#' or '\\')
            {
                value = value[..i];
                break;
            }
        }

        if (value.Length == 0)
            return value;

        if (value[0] is '[')
            return CanonicalizeIPv6Hostname(value);

        var domain = PercentEncoding.Decode(value);
        var asciiDomain = DomainToAscii(domain);

        foreach (var c in asciiDomain)
        {
            if (IsForbiddenDomainCodePoint(c))
                throw new UrlPatternException($"Invalid hostname: '{value}' contains '{c}'");
        }

        return asciiDomain;
    }

    /// <summary>Canonicalizes an IPv6 hostname.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#canon-an-ipv6-hostname">WHATWG URL Pattern Spec - Canonicalize an IPv6 hostname</see>
    /// </remarks>
    public static string CanonicalizeIPv6Hostname(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c) && c is not ('[' or ']' or ':'))
                throw new UrlPatternException($"Invalid IPv6 hostname: '{value}' contains '{c}'");
        }

        return value.ToLowerInvariant();
    }

    /// <summary>Canonicalizes a port, dropping it when it is the default port of <paramref name="protocolValue"/>.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#canon-a-port">WHATWG URL Pattern Spec - Canonicalize a port</see>
    /// </remarks>
    public static string CanonicalizePort(string portValue, string? protocolValue = null)
    {
        if (portValue.Length == 0)
            return portValue;

        portValue = RemoveTabsAndNewlines(portValue);

        // Port state stops at the first code point that is not an ASCII digit, because a state override
        // was given, and keeps whatever digits it had read so far. It only fails when there were none
        var digitCount = 0;
        while (digitCount < portValue.Length && char.IsAsciiDigit(portValue[digitCount]))
        {
            digitCount++;
        }

        if (digitCount == 0)
            throw new UrlPatternException($"Invalid port: '{portValue}' does not start with a digit");

        if (!ushort.TryParse(portValue.AsSpan(0, digitCount), NumberStyles.None, CultureInfo.InvariantCulture, out var port))
            throw new UrlPatternException($"Invalid port: '{portValue}' is greater than {ushort.MaxValue}");

        var serialized = port.ToString(CultureInfo.InvariantCulture);
        if (protocolValue is not null &&
            SpecialSchemes.TryGetDefaultPort(protocolValue, out var defaultPort) &&
            serialized == defaultPort)
        {
            return "";
        }

        return serialized;
    }

    /// <summary>Canonicalizes the pathname of a URL with a special scheme.</summary>
    /// <remarks>
    /// <para>
    /// The URL parser prepends a leading "/" to a path, which must not happen here because this runs on
    /// each fixed-text part of a pattern rather than on the whole pathname: it would turn "/books/:id.json"
    /// into "/books/:id/.json". The spec sidesteps it by prefixing "/-" and removing it from the result;
    /// the "-" is there so that a value starting with "." is not read as a "/." segment and collapsed.
    /// </para>
    /// <see href="https://urlpattern.spec.whatwg.org/#canon-a-pathname">WHATWG URL Pattern Spec - Canonicalize a pathname</see>
    /// </remarks>
    public static string CanonicalizePathname(string value)
    {
        if (value.Length == 0)
            return value;

        var leadingSlash = value[0] is '/';
        var modifiedValue = RemoveTabsAndNewlines(leadingSlash ? value : "/-" + value);

        var result = SerializePath(ParsePath(modifiedValue));

        return leadingSlash ? result : result[2..];
    }

    /// <summary>Canonicalizes the pathname of a URL whose scheme is not a special scheme.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#canon-an-opaque-pathname">WHATWG URL Pattern Spec - Canonicalize an opaque pathname</see>
    /// </remarks>
    public static string CanonicalizeOpaquePathname(string value)
    {
        if (value.Length == 0)
            return value;

        value = RemoveTabsAndNewlines(value);

        var builder = new StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            var c = value[index];

            // Opaque path state moves on to the query or the fragment, neither of which is part of the result
            if (c is '?' or '#')
                break;

            if (c is ' ')
            {
                // A space is only encoded when it would otherwise end up trailing the path
                var next = index + 1 < value.Length ? value[index + 1] : '\0';
                builder.Append(next is '?' or '#' ? "%20" : " ");
                index++;
                continue;
            }

            index += AppendEncodedCodePoint(builder, value.AsSpan(index), PercentEncodeSet.C0Control);
        }

        return builder.ToString();
    }

    /// <summary>Canonicalizes a search.</summary>
    /// <remarks>
    /// The dummy URL record has a special scheme, so the special-query percent-encode set applies and a
    /// "'" is encoded as well.
    /// <see href="https://urlpattern.spec.whatwg.org/#canon-a-search">WHATWG URL Pattern Spec - Canonicalize a search</see>
    /// </remarks>
    public static string CanonicalizeSearch(string value)
    {
        return PercentEncoding.Encode(RemoveTabsAndNewlines(value), PercentEncodeSet.SpecialQuery);
    }

    /// <summary>Canonicalizes a hash.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#canon-a-hash">WHATWG URL Pattern Spec - Canonicalize a hash</see>
    /// </remarks>
    public static string CanonicalizeHash(string value)
    {
        return PercentEncoding.Encode(RemoveTabsAndNewlines(value), PercentEncodeSet.Fragment);
    }

    /// <summary>The code points the basic URL parser removes from its input before doing anything else.</summary>
    private static string RemoveTabsAndNewlines(string value)
    {
        if (value.AsSpan().IndexOfAny(TabsAndNewlines) < 0)
            return value;

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is not ('\t' or '\n' or '\r'))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Serializes an IPv6 address the way the URL Standard does.</summary>
    /// <remarks>
    /// <see cref="Uri"/> writes the last two pieces of an address such as "::ab:1" in the dotted-decimal
    /// form ("::0.171.0.1"), which the URL Standard never does, so a host read from a <see cref="Uri"/>
    /// has to be written out again to be comparable with a pattern.
    /// <see href="https://url.spec.whatwg.org/#concept-ipv6-serializer">URL Standard - IPv6 serializer</see>
    /// </remarks>
    public static string SerializeIPv6Hostname(string value)
    {
        var address = value.AsSpan().Trim(['[', ']']);
        if (!IPAddress.TryParse(address, out var parsed) || parsed.AddressFamily is not AddressFamily.InterNetworkV6)
            return value.ToLowerInvariant();

        Span<byte> bytes = stackalloc byte[16];
        if (!parsed.TryWriteBytes(bytes, out _))
            return value.ToLowerInvariant();

        Span<ushort> pieces = stackalloc ushort[8];
        for (var i = 0; i < pieces.Length; i++)
        {
            pieces[i] = (ushort)((bytes[i * 2] << 8) | bytes[(i * 2) + 1]);
        }

        // The longest run of zero pieces is replaced by "::", but only when it covers more than one piece
        var compress = -1;
        var compressLength = 1;
        for (var i = 0; i < pieces.Length; i++)
        {
            if (pieces[i] is not 0)
                continue;

            var length = 0;
            while (i + length < pieces.Length && pieces[i + length] is 0)
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
        for (var i = 0; i < pieces.Length; i++)
        {
            if (ignoreZeroes)
            {
                if (pieces[i] is 0)
                    continue;

                ignoreZeroes = false;
            }

            if (i == compress)
            {
                builder.Append(i is 0 ? "::" : ":");
                ignoreZeroes = true;
                continue;
            }

            builder.Append(pieces[i].ToString("x", CultureInfo.InvariantCulture));
            if (i is not 7)
            {
                builder.Append(':');
            }
        }

        return builder.Append(']').ToString();
    }

    /// <summary>Removes the leading C0 controls and spaces, as the basic URL parser does when no URL record is given.</summary>
    private static string TrimLeadingC0ControlsAndSpaces(string value)
    {
        var index = 0;
        while (index < value.Length && value[index] <= ' ')
        {
            index++;
        }

        return value[index..];
    }

    /// <summary>Runs the path start state followed by the path state, for a URL with a special scheme.</summary>
    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#path-state">URL Standard - Path state</see>
    /// </remarks>
    private static List<string> ParsePath(string input)
    {
        var path = new List<string>();
        var buffer = new StringBuilder();
        var index = 0;

        // Path start state: the separator that opens the path is consumed here rather than by the path state
        if (index < input.Length && input[index] is '/' or '\\')
        {
            index++;
        }

        while (true)
        {
            if (index >= input.Length)
            {
                FlushSegment(path, buffer, atSegmentSeparator: false);
                break;
            }

            // A backslash separates segments too, because the dummy URL record has a special scheme
            if (input[index] is '/' or '\\')
            {
                FlushSegment(path, buffer, atSegmentSeparator: true);
                index++;
                continue;
            }

            index += AppendEncodedCodePoint(buffer, input.AsSpan(index), PercentEncodeSet.Path);
        }

        return path;
    }

    private static void FlushSegment(List<string> path, StringBuilder buffer, bool atSegmentSeparator)
    {
        var segment = buffer.ToString();
        buffer.Clear();

        if (IsDoubleDotSegment(segment))
        {
            if (path.Count > 0)
            {
                path.RemoveAt(path.Count - 1);
            }

            // "/a/.." keeps a trailing empty segment so that it serializes as "/" rather than as nothing
            if (!atSegmentSeparator)
            {
                path.Add("");
            }
        }
        else if (IsSingleDotSegment(segment))
        {
            if (!atSegmentSeparator)
            {
                path.Add("");
            }
        }
        else
        {
            path.Add(segment);
        }
    }

    private static string SerializePath(List<string> path)
    {
        var builder = new StringBuilder();
        foreach (var segment in path)
        {
            builder.Append('/').Append(segment);
        }

        return builder.ToString();
    }

    private static bool IsSingleDotSegment(string segment)
    {
        return segment is "." || segment.Equals("%2e", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDoubleDotSegment(string segment)
    {
        return segment is ".." ||
            segment.Equals(".%2e", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("%2e.", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("%2e%2e", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Percent-encodes the code point at the start of <paramref name="value"/> and returns how many chars it spanned.</summary>
    private static int AppendEncodedCodePoint(StringBuilder builder, ReadOnlySpan<char> value, PercentEncodeSet set)
    {
        // An unpaired surrogate decodes to U+FFFD, which is how a DOMString becomes a USVString
        Rune.DecodeFromUtf16(value, out var rune, out var consumed);
        PercentEncoding.EncodeCodePoint(builder, rune, set);

        return consumed;
    }

    /// <summary>Runs the "domain to ASCII" operation of the URL Standard.</summary>
    /// <remarks>
    /// Unicode ToASCII is defined label by label, so an empty label (which a fixed-text part such as "."
    /// consists of) has to stay empty instead of failing.
    /// <see href="https://url.spec.whatwg.org/#concept-domain-to-ascii">URL Standard - Domain to ASCII</see>
    /// </remarks>
    private static string DomainToAscii(string domain)
    {
        if (Ascii.IsValid(domain))
            return domain.ToLowerInvariant();

        var labels = domain.Split('.');
        for (var i = 0; i < labels.Length; i++)
        {
            if (Ascii.IsValid(labels[i]))
            {
                labels[i] = labels[i].ToLowerInvariant();
                continue;
            }

            try
            {
                labels[i] = IdnMapping.GetAscii(labels[i]);
            }
            catch (ArgumentException ex)
            {
                throw new UrlPatternException($"Invalid hostname: '{domain}'", ex);
            }
        }

        return string.Join('.', labels);
    }

    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#forbidden-domain-code-point">URL Standard - Forbidden domain code point</see>
    /// </remarks>
    private static bool IsForbiddenDomainCodePoint(char c)
    {
        // Forbidden host code points, plus the C0 controls, '%' and U+007F DELETE
        return c <= 0x20 ||
            c is '#' or '%' or '/' or ':' or '<' or '>' or '?' or '@' or '[' or '\\' or ']' or '^' or '|' or (char)0x7F;
    }
}
