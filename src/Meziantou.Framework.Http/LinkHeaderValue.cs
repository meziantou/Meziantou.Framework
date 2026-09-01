using System.Buffers;
using System.Net.Http.Headers;

namespace Meziantou.Framework.Http;

/// <summary>
/// Represents a Link header value as defined in <see href="https://datatracker.ietf.org/doc/html/rfc8288">RFC 8288</see>.
/// </summary>
public sealed class LinkHeaderValue
{
    private static ReadOnlySpan<char> WhiteSpaceCharacters => [' ', '\t', '\r', '\n'];
    private static ReadOnlySpan<char> ParameterSeparators => [' ', '\t', '\r', '\n', '=', ';', ','];

    /// <summary>Gets the URL of the link.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Breaking change")]
    public string Url { get; }

    /// <summary>Gets the relation type (rel parameter) of the link.</summary>
    public string Rel => GetParameterValue("rel") ?? "";

    /// <summary>Gets the value of a parameter by name. The comparison is case-insensitive.</summary>
    /// <param name="parameterName">The name of the parameter.</param>
    /// <returns>The parameter value, or <see langword="null"/> if the parameter is not found.</returns>
    public string? GetParameterValue(string parameterName)
    {
        var parameters = Parameters;
        for (var i = 0; i < parameters.Count; i++)
        {
            if (string.Equals(parameters[i].Key, parameterName, StringComparison.OrdinalIgnoreCase))
                return parameters[i].Value;
        }

        return null;
    }

    /// <summary>Gets the list of parameters associated with the link.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; }

    private LinkHeaderValue(string url, IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(parameters);

        Url = url;
        Parameters = parameters;
    }

    /// <summary>Parses Link header values from an HTTP response message.</summary>
    /// <param name="httpResponse">The HTTP response message containing Link headers.</param>
    /// <returns>A collection of <see cref="LinkHeaderValue"/> instances.</returns>
    // https://httpwg.org/specs/rfc8288.html
    // https://datatracker.ietf.org/doc/html/rfc7230#section-3.2.3
    public static IEnumerable<LinkHeaderValue> Parse(HttpResponseMessage httpResponse)
    {
        ArgumentNullException.ThrowIfNull(httpResponse);

        return Parse(httpResponse.Headers);
    }

    /// <summary>Parses Link header values from HTTP headers.</summary>
    /// <param name="headers">The HTTP headers containing Link header values.</param>
    /// <returns>A collection of <see cref="LinkHeaderValue"/> instances.</returns>
    public static IEnumerable<LinkHeaderValue> Parse(HttpHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (!headers.TryGetValues("Link", out var values))
            return [];

        return values.SelectMany(Parse);
    }

    /// <summary>Parses a Link header value from a string.</summary>
    /// <param name="value">The Link header value to parse.</param>
    /// <returns>A collection of <see cref="LinkHeaderValue"/> instances.</returns>
    public static IEnumerable<LinkHeaderValue> Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Parse(value.AsSpan());
    }

    /// <summary>Parses a Link header value from a character span.</summary>
    /// <param name="value">The Link header value to parse.</param>
    /// <returns>A collection of <see cref="LinkHeaderValue"/> instances.</returns>
    public static IEnumerable<LinkHeaderValue> Parse(ReadOnlySpan<char> value)
    {
        var result = new List<LinkHeaderValue>();
        while (!value.IsEmpty)
        {
            value = ConsumeOptionalWhiteSpaces(value);
            if (value.IsEmpty)
                break;

            if (value[0] is not '<')
            {
                // Not a link-value. Ignore it and resume at the next one rather than dropping the rest
                // of the header: RFC 7230 section 7 requires empty list elements to be ignored.
                value = SkipToNextLinkValue(value);
                continue;
            }

            // Remove the first '<'
            value = value[1..];

            // A URI-Reference cannot contain '<', so one appearing before the closing '>' means the
            // current link-value is unterminated and that '<' starts the next one. Commas are legal
            // inside a URI, so they cannot be used to delimit the target here.
            var index = value.IndexOfAny('>', '<');
            if (index == -1 || value[index] is '<')
            {
                value = SkipToNextLinkValue(value);
                continue;
            }

            var targetLink = value[..index].Trim().ToString();
            value = value[(index + 1)..];

            // Parse parameters
            var parameters = new List<KeyValuePair<string, string>>();
            while (!value.IsEmpty && value[0] != ',')
            {
                value = ConsumeOptionalWhiteSpaces(value);
                if (value.IsEmpty)
                    break;

                if (value[0] is not ';')
                    break;

                value = value[1..];
                value = ConsumeOptionalWhiteSpaces(value);
                index = value.IndexOfAny(ParameterSeparators);

                ReadOnlySpan<char> parameterName;
                var parameterValue = "";
                if (index == -1)
                {
                    parameterName = value;
                    value = [];
                }
                else
                {
                    parameterName = value[0..index];
                    value = value[index..];
                }

                value = ConsumeOptionalWhiteSpaces(value);
                if (value.Length > 0 && value[0] is '=')
                {
                    value = value[1..];
                    value = ConsumeOptionalWhiteSpaces(value);
                    if (value.Length > 0 && value[0] is '"')
                    {
                        value = value[1..];
                        var specialIndex = value.IndexOfAny('"', '\\');
                        if (specialIndex == -1)
                        {
                            // Unterminated quoted string: take the remaining characters
                            parameterValue = value.ToString();
                            value = [];
                        }
                        else if (value[specialIndex] is '"')
                        {
                            // No escape sequence: slice the value directly without a StringBuilder
                            parameterValue = value[..specialIndex].ToString();
                            value = value[(specialIndex + 1)..];
                        }
                        else
                        {
                            // Contains at least one escape sequence
                            var sb = new StringBuilder();
                            sb.Append(value[..specialIndex]);
                            value = value[specialIndex..];
                            while (!value.IsEmpty)
                            {
                                var c = value[0];
                                if (c == '"')
                                {
                                    value = value[1..];
                                    break;
                                }
                                else if (c == '\\')
                                {
                                    if (value.Length > 1)
                                    {
                                        sb.Append(value[1]);
                                        value = value[2..];
                                    }
                                    else
                                    {
                                        value = value[1..];
                                        break;
                                    }
                                }
                                else
                                {
                                    // Append the whole run of regular characters at once
                                    var next = value.IndexOfAny('"', '\\');
                                    if (next == -1)
                                    {
                                        sb.Append(value);
                                        value = [];
                                    }
                                    else
                                    {
                                        sb.Append(value[..next]);
                                        value = value[next..];
                                    }
                                }
                            }

                            parameterValue = sb.ToString();
                        }
                    }
                    else
                    {
                        index = value.IndexOfAny(';', ',');
                        if (index == -1)
                        {
                            // Trim the trailing whitespace: an unquoted value is terminated by the next
                            // ';' or ',', and RFC 7230 allows optional whitespace before that separator.
                            parameterValue = value.Trim().ToString();
                            value = [];
                        }
                        else
                        {
                            parameterValue = value[0..index].Trim().ToString();
                            value = value[index..];
                        }
                    }
                }

                // A separator right after ';' yields no name at all (";;" or ";=value"). Skip it rather
                // than recording a phantom parameter that callers enumerating Parameters would see.
                if (!parameterName.IsEmpty)
                {
                    parameters.Add(KeyValuePair.Create(ToLowerInvariantString(parameterName), parameterValue));
                }

                value = ConsumeOptionalWhiteSpaces(value);
            }

            result.Add(new LinkHeaderValue(targetLink, parameters));

            if (value.Length > 0 && value[0] is ',')
            {
                value = value[1..];
            }
        }

        return result;

        static ReadOnlySpan<char> ConsumeOptionalWhiteSpaces(ReadOnlySpan<char> value)
        {
            var index = value.IndexOfAnyExcept(WhiteSpaceCharacters);
            return index == -1 ? [] : value[index..];
        }

        // Resumes after the next ',' that is not inside a quoted string, so a malformed link-value
        // only discards itself instead of everything that follows it.
        static ReadOnlySpan<char> SkipToNextLinkValue(ReadOnlySpan<char> value)
        {
            var inQuotes = false;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (inQuotes)
                {
                    if (c is '\\')
                    {
                        i++;
                    }
                    else if (c is '"')
                    {
                        inQuotes = false;
                    }
                }
                else if (c is '"')
                {
                    inQuotes = true;
                }
                else if (c is ',')
                {
                    return value[(i + 1)..];
                }
            }

            return [];
        }
    }

    // Lowercases the span using the invariant culture, allocating a single string and avoiding
    // any work when the value is already lowercase (the common case for header parameter names).
    private static string ToLowerInvariantString(ReadOnlySpan<char> value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.ToLowerInvariant(rune) != rune)
                return ToLowerInvariantSlow(value);
        }

        return value.ToString();

        static string ToLowerInvariantSlow(ReadOnlySpan<char> value)
        {
            char[]? rented = null;
            Span<char> buffer = value.Length <= 256
                ? stackalloc char[256]
                : (rented = ArrayPool<char>.Shared.Rent(value.Length));

            var written = value.ToLowerInvariant(buffer);

            // ToLowerInvariant returns -1 when the destination is too small. We size the buffer to the
            // source length, which is enough today, but don't assume the lowercased form has the same
            // length: fall back to allocating a string when it doesn't fit.
            var result = written < 0
                ? value.ToString().ToLowerInvariant()
                : new string(buffer[..written]);

            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);

            return result;
        }
    }
}
