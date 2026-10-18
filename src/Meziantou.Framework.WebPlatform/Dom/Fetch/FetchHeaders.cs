using System.Text;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The algorithms of the headers of the Fetch Standard (https://fetch.spec.whatwg.org/#terminology-headers) used by
/// <see cref="Headers"/>, <see cref="XmlHttpRequest"/> and the fetch algorithm. The names and the values are byte strings:
/// .NET strings whose characters are in the range U+0000 to U+00FF (the isomorphic decoding of the bytes).
/// </summary>
internal static class FetchHeaders
{
    /// <summary>https://fetch.spec.whatwg.org/#forbidden-request-header</summary>
    private static readonly HashSet<string> ForbiddenRequestHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept-Charset", "Accept-Encoding", "Access-Control-Request-Headers", "Access-Control-Request-Method", "Connection",
        "Content-Length", "Cookie", "Cookie2", "Date", "DNT", "Expect", "Host", "Keep-Alive", "Origin", "Referer", "Set-Cookie",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Via",
    };

    /// <summary>https://fetch.spec.whatwg.org/#cors-safelisted-response-header-name</summary>
    private static readonly HashSet<string> CorsSafelistedResponseHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cache-Control", "Content-Language", "Content-Length", "Content-Type", "Expires", "Last-Modified", "Pragma",
    };

    /// <summary>https://fetch.spec.whatwg.org/#request-body-header-name</summary>
    public static readonly string[] RequestBodyHeaderNames = ["Content-Encoding", "Content-Language", "Content-Location", "Content-Type"];

    /// <summary>https://fetch.spec.whatwg.org/#http-whitespace-byte</summary>
    public static bool IsHttpWhitespace(char c) => c is '\t' or '\n' or '\r' or ' ';

    /// <summary>https://fetch.spec.whatwg.org/#concept-header-value-normalize: removes the leading and trailing HTTP whitespace bytes.</summary>
    public static string Normalize(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsHttpWhitespace(value[start]))
        {
            start++;
        }

        while (end > start && IsHttpWhitespace(value[end - 1]))
        {
            end--;
        }

        return start == 0 && end == value.Length ? value : value[start..end];
    }

    /// <summary>https://fetch.spec.whatwg.org/#header-name: a token (https://httpwg.org/specs/rfc9110.html#fields.names).</summary>
    public static bool IsHeaderName(string name) => IsToken(name);

    /// <summary>https://httpwg.org/specs/rfc9110.html#tokens</summary>
    public static bool IsToken(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (var c in value)
        {
            if (!IsTokenCharacter(c))
                return false;
        }

        return true;
    }

    internal static bool IsTokenCharacter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
        or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';

    /// <summary>
    /// https://fetch.spec.whatwg.org/#header-value: no leading or trailing HTTP tab or space byte, and no 0x00 (NUL), 0x0A (LF)
    /// or 0x0D (CR) byte.
    /// </summary>
    public static bool IsHeaderValue(string value)
    {
        if (value.Length > 0 && (value[0] is '\t' or ' ' || value[^1] is '\t' or ' '))
            return false;

        return value.AsSpan().IndexOfAny('\0', '\n', '\r') < 0;
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-method</summary>
    public static bool IsMethod(string method) => IsToken(method);

    /// <summary>https://fetch.spec.whatwg.org/#forbidden-method</summary>
    public static bool IsForbiddenMethod(string method) => InfraAscii.EqualsIgnoreAsciiCase(method, "CONNECT") || InfraAscii.EqualsIgnoreAsciiCase(method, "TRACE") || InfraAscii.EqualsIgnoreAsciiCase(method, "TRACK");

    /// <summary>https://fetch.spec.whatwg.org/#cors-safelisted-method</summary>
    public static bool IsCorsSafelistedMethod(string method) => method is "GET" or "HEAD" or "POST";

    /// <summary>https://fetch.spec.whatwg.org/#concept-method-normalize</summary>
    public static string NormalizeMethod(string method)
    {
        foreach (var normalized in (ReadOnlySpan<string>)["DELETE", "GET", "HEAD", "OPTIONS", "POST", "PUT"])
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(method, normalized))
                return normalized;
        }

        return method;
    }

    /// <summary>https://fetch.spec.whatwg.org/#forbidden-request-header</summary>
    public static bool IsForbiddenRequestHeader(string name, string value)
    {
        if (ForbiddenRequestHeaderNames.Contains(name))
            return true;

        if (name.StartsWith("proxy-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("sec-", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.Equals("X-HTTP-Method", StringComparison.OrdinalIgnoreCase) || name.Equals("X-HTTP-Method-Override", StringComparison.OrdinalIgnoreCase) || name.Equals("X-Method-Override", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var method in HeaderList.GetDecodeAndSplit([new(name, value)], name)!)
            {
                if (IsForbiddenMethod(method))
                    return true;
            }
        }

        return false;
    }

    /// <summary>https://fetch.spec.whatwg.org/#forbidden-response-header-name</summary>
    public static bool IsForbiddenResponseHeaderName(string name) => name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) || name.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase);

    /// <summary>https://fetch.spec.whatwg.org/#no-cors-safelisted-request-header-name</summary>
    public static bool IsNoCorsSafelistedRequestHeaderName(string name) => name.Equals("Accept", StringComparison.OrdinalIgnoreCase) || name.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Content-Language", StringComparison.OrdinalIgnoreCase) || name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase);

    /// <summary>https://fetch.spec.whatwg.org/#no-cors-safelisted-request-header</summary>
    public static bool IsNoCorsSafelistedRequestHeader(string name, string value) => IsNoCorsSafelistedRequestHeaderName(name) && IsCorsSafelistedRequestHeader(name, value);

    /// <summary>https://fetch.spec.whatwg.org/#privileged-no-cors-request-header-name</summary>
    public static bool IsPrivilegedNoCorsRequestHeaderName(string name) => name.Equals("Range", StringComparison.OrdinalIgnoreCase);

    /// <summary>https://fetch.spec.whatwg.org/#cors-non-wildcard-request-header-name</summary>
    public static bool IsCorsNonWildcardRequestHeaderName(string name) => name.Equals("Authorization", StringComparison.OrdinalIgnoreCase);

    /// <summary>https://fetch.spec.whatwg.org/#cors-unsafe-request-header-byte</summary>
    private static bool IsCorsUnsafeRequestHeaderByte(char c) => (c < 0x20 && c != '\t') || c is '"' or '(' or ')' or ':' or '<' or '>' or '?' or '@' or '[' or '\\' or ']' or '{' or '}' or (char)0x7F;

    private static bool ContainsCorsUnsafeRequestHeaderByte(string value)
    {
        foreach (var c in value)
        {
            if (IsCorsUnsafeRequestHeaderByte(c))
                return true;
        }

        return false;
    }

    /// <summary>https://fetch.spec.whatwg.org/#cors-safelisted-request-header</summary>
    public static bool IsCorsSafelistedRequestHeader(string name, string value)
    {
        if (value.Length > 128)
            return false;

        switch (name.ToUpperInvariant())
        {
            case "ACCEPT":
                return !ContainsCorsUnsafeRequestHeaderByte(value);

            case "ACCEPT-LANGUAGE":
            case "CONTENT-LANGUAGE":
                foreach (var c in value)
                {
                    if (c is not (>= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or ' ' or '*' or ',' or '-' or '.' or ';' or '='))
                        return false;
                }

                return true;

            case "CONTENT-TYPE":
                if (ContainsCorsUnsafeRequestHeaderByte(value))
                    return false;

                return MimeType.Parse(value) is { Essence: "application/x-www-form-urlencoded" or "multipart/form-data" or "text/plain" };

            case "RANGE":
                // https://fetch.spec.whatwg.org/#simple-range-header-value: a single range whose start is present
                return ParseSingleRangeHeaderValue(value, allowWhitespace: false) is ({ }, _);

            default:
                return false;
        }
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#simple-range-header-value: returns the start and the end of the range, or null on
    /// failure.
    /// </summary>
    public static (long? Start, long? End)? ParseSingleRangeHeaderValue(string value, bool allowWhitespace)
    {
        var position = 0;
        // The unit is compared as it is: "Bytes=0-" is a failure
        if (!value.StartsWith("bytes", StringComparison.Ordinal))
            return null;

        position += 5;
        SkipWhitespace(value, ref position, allowWhitespace);
        if (position >= value.Length || value[position] != '=')
            return null;

        position++;
        SkipWhitespace(value, ref position, allowWhitespace);
        var start = CollectDigits(value, ref position);
        SkipWhitespace(value, ref position, allowWhitespace);
        if (position >= value.Length || value[position] != '-')
            return null;

        position++;
        SkipWhitespace(value, ref position, allowWhitespace);
        var end = CollectDigits(value, ref position);
        if (position < value.Length)
            return null;

        if (start is null && end is null)
            return null;

        if (start is { } s && end is { } e && s > e)
            return null;

        return (start, end);

        static void SkipWhitespace(string value, ref int position, bool allowWhitespace)
        {
            while (allowWhitespace && position < value.Length && value[position] is '\t' or ' ')
            {
                position++;
            }
        }

        static long? CollectDigits(string value, ref int position)
        {
            var start = position;
            while (position < value.Length && value[position] is >= '0' and <= '9')
            {
                position++;
            }

            if (position == start)
                return null;

            return long.TryParse(value.AsSpan(start, position - start), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : long.MaxValue;
        }
    }

    /// <summary>https://fetch.spec.whatwg.org/#cors-unsafe-request-header-names: the sorted lowercase names.</summary>
    public static List<string> GetCorsUnsafeRequestHeaderNames(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        var unsafeNames = new List<string>();
        var potentiallyUnsafeNames = new List<string>();
        var safelistValueSize = 0;
        foreach (var (name, value) in headers)
        {
            if (!IsCorsSafelistedRequestHeader(name, value))
            {
                unsafeNames.Add(name);
            }
            else
            {
                potentiallyUnsafeNames.Add(name);
                safelistValueSize += value.Length;
            }
        }

        if (safelistValueSize > 1024)
        {
            unsafeNames.AddRange(potentiallyUnsafeNames);
        }

        return ConvertToSortedLowercaseSet(unsafeNames);
    }

    /// <summary>https://fetch.spec.whatwg.org/#convert-header-names-to-a-sorted-lowercase-set</summary>
    public static List<string> ConvertToSortedLowercaseSet(IEnumerable<string> names)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            set.Add(InfraAscii.ToAsciiLowercase(name));
        }

        return [.. set];
    }

    /// <summary>https://fetch.spec.whatwg.org/#cors-safelisted-response-header-name</summary>
    public static bool IsCorsSafelistedResponseHeaderName(string name, IReadOnlyList<string> exposedNames)
    {
        if (CorsSafelistedResponseHeaderNames.Contains(name))
            return true;

        foreach (var exposedName in exposedNames)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(exposedName, name) && !IsForbiddenResponseHeaderName(name))
                return true;
        }

        return false;
    }

    /// <summary>Whether the header list contains a header whose name matches <paramref name="name"/> (https://fetch.spec.whatwg.org/#header-list-contains).</summary>
    public static bool Contains(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
    {
        foreach (var header in headers)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(header.Key, name))
                return true;
        }

        return false;
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-header-list-delete</summary>
    public static void Delete(List<KeyValuePair<string, string>> headers, string name)
    {
        for (var i = headers.Count - 1; i >= 0; i--)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(headers[i].Key, name))
            {
                headers.RemoveAt(i);
            }
        }
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-header-list-append: the name of the first header with the same name is used.</summary>
    public static void Append(List<KeyValuePair<string, string>> headers, string name, string value)
    {
        foreach (var header in headers)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(header.Key, name))
            {
                name = header.Key;
                break;
            }
        }

        headers.Add(new(name, value));
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-header-list-set: replaces the first header with the name, and removes the others.</summary>
    public static void Set(List<KeyValuePair<string, string>> headers, string name, string value)
    {
        var index = IndexOf(headers, name);
        if (index < 0)
        {
            headers.Add(new(name, value));
            return;
        }

        headers[index] = new(headers[index].Key, value);
        for (var i = headers.Count - 1; i > index; i--)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(headers[i].Key, name))
            {
                headers.RemoveAt(i);
            }
        }
    }

    /// <summary>The index of the first header whose name is <paramref name="name"/> (ASCII case-insensitive), or -1.</summary>
    public static int IndexOf(List<KeyValuePair<string, string>> headers, string name)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(headers[i].Key, name))
                return i;
        }

        return -1;
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-header-list-combine</summary>
    public static void Combine(List<KeyValuePair<string, string>> headers, string name, string value)
    {
        var index = IndexOf(headers, name);
        if (index < 0)
        {
            headers.Add(new(name, value));
            return;
        }

        headers[index] = new(headers[index].Key, headers[index].Value + ", " + value);
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-header-list-sort-and-combine</summary>
    public static List<KeyValuePair<string, string>> SortAndCombine(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        // The values of each lowercase name, in the order of the list
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, value) in headers)
        {
            var lowercaseName = InfraAscii.ToAsciiLowercase(name);
            if (!groups.TryGetValue(lowercaseName, out var values))
            {
                values = [];
                groups.Add(lowercaseName, values);
            }

            values.Add(value);
        }

        var result = new List<KeyValuePair<string, string>>(groups.Count);
        foreach (var (name, values) in groups.OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            // The values of Set-Cookie are not combined
            if (name == "set-cookie")
            {
                foreach (var value in values)
                {
                    result.Add(new(name, value));
                }
            }
            else
            {
                result.Add(new(name, string.Join(", ", values)));
            }
        }

        return result;
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#extract-header-list-values for the headers whose values are lists of tokens (e.g.
    /// <c>Access-Control-Allow-Methods</c>): null when there is no such header, an empty array on failure (a value that is
    /// not a token).
    /// </summary>
    public static List<string>? ExtractTokenList(IReadOnlyList<KeyValuePair<string, string>> headers, string name, out bool failure)
    {
        failure = false;
        List<string>? values = null;
        foreach (var (headerName, headerValue) in headers)
        {
            if (!InfraAscii.EqualsIgnoreAsciiCase(headerName, name))
                continue;

            values ??= [];
            foreach (var part in headerValue.Split(','))
            {
                var value = Normalize(part);
                if (value.Length == 0)
                    continue;

                if (!IsToken(value))
                {
                    failure = true;
                    return null;
                }

                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>The serialization of the header names (e.g. for <c>Access-Control-Request-Headers</c>).</summary>
    public static string Join(IEnumerable<string> values) => string.Join(',', values);

    /// <summary>The isomorphic encoding of a byte string (https://infra.spec.whatwg.org/#isomorphic-encode).</summary>
    public static byte[] IsomorphicEncode(string value) => Encoding.Latin1.GetBytes(value);

    /// <summary>The isomorphic decoding of bytes (https://infra.spec.whatwg.org/#isomorphic-decode).</summary>
    public static string IsomorphicDecode(ReadOnlySpan<byte> value) => Encoding.Latin1.GetString(value);
}
