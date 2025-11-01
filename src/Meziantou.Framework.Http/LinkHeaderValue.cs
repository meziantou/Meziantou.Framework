using System.Net.Http.Headers;

namespace Meziantou.Framework.Http;

/// <summary>
/// Represents a parsed Link HTTP header value as defined in RFC 8288.
/// </summary>
public sealed class LinkHeaderValue
{
    private static ReadOnlySpan<char> ParameterSeparators => [' ', '\t', '=', ';', ','];

    /// <summary>
    /// Gets the URL specified in the Link header.
    /// </summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Breaking change")]
    public string Url { get; }

    /// <summary>
    /// Gets the relationship type (rel parameter) of the link.
    /// </summary>
    public string Rel => GetParameterValue("rel") ?? "";

    /// <summary>
    /// Gets the value of a parameter by name.
    /// </summary>
    /// <param name="parameterName">The name of the parameter to retrieve.</param>
    /// <returns>The parameter value, or <see langword="null"/> if the parameter is not found.</returns>
    public string? GetParameterValue(string parameterName) => Parameters.FirstOrDefault(p => p.Key == parameterName).Value;

    /// <summary>
    /// Gets the parameters associated with the link.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; }

    private LinkHeaderValue(string url, IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(parameters);

        Url = url;
        Parameters = parameters;
    }

    /// <summary>
    /// Parses Link headers from an HTTP response message.
    /// </summary>
    /// <param name="httpResponse">The HTTP response message containing Link headers.</param>
    /// <returns>A collection of parsed <see cref="LinkHeaderValue"/> instances.</returns>
    /// <remarks>
    /// Based on <see href="https://httpwg.org/specs/rfc8288.html">RFC 8288</see> and <see href="https://datatracker.ietf.org/doc/html/rfc7230#section-3.2.3">RFC 7230 Section 3.2.3</see>.
    /// </remarks>
    public static IEnumerable<LinkHeaderValue> Parse(HttpResponseMessage httpResponse) => Parse(httpResponse.Headers);

    /// <summary>
    /// Parses Link headers from HTTP headers.
    /// </summary>
    /// <param name="headers">The HTTP headers containing Link headers.</param>
    /// <returns>A collection of parsed <see cref="LinkHeaderValue"/> instances.</returns>
    public static IEnumerable<LinkHeaderValue> Parse(HttpHeaders headers)
    {
        if (!headers.TryGetValues("Link", out var values))
            return [];

        return values.SelectMany(Parse);
    }

    /// <summary>
    /// Parses a Link header value string.
    /// </summary>
    /// <param name="value">The Link header value to parse.</param>
    /// <returns>A collection of parsed <see cref="LinkHeaderValue"/> instances.</returns>
    public static IEnumerable<LinkHeaderValue> Parse(string value) => Parse(value.AsSpan());

    /// <summary>
    /// Parses a Link header value from a character span.
    /// </summary>
    /// <param name="value">The Link header value to parse.</param>
    /// <returns>A collection of parsed <see cref="LinkHeaderValue"/> instances.</returns>
    public static IEnumerable<LinkHeaderValue> Parse(ReadOnlySpan<char> value)
    {
        var result = new List<LinkHeaderValue>();
        while (!value.IsEmpty)
        {
            value = ConsumeOptionalWhiteSpaces(value);
            if (value.IsEmpty)
                break;

            if (value[0] is not '<')
                break;

            // Remove the first '<'
            value = value[1..];
            var index = value.IndexOf('>');
            if (index == -1)
                break;

            var targetLink = value[..index].ToString();
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

                string parameterName;
                var parameterValue = "";
                if (index == -1)
                {
                    parameterName = value.ToString();
                    value = [];
                }
                else
                {
                    parameterName = value[0..index].ToString();
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
                        var sb = new StringBuilder();
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
                                if (value.Length > 0)
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
                                sb.Append(c);
                                value = value[1..];
                            }
                        }

                        parameterValue = sb.ToString();
                    }
                    else
                    {
                        index = value.IndexOfAny(';', ',');
                        if (index == -1)
                        {
                            parameterValue = value.ToString();
                            value = [];
                        }
                        else
                        {
                            parameterValue = value[0..index].ToString();
                            value = value[index..];
                        }
                    }
                }

#pragma warning disable CA1308 // Normalize strings to uppercase
                parameters.Add(KeyValuePair.Create(parameterName.ToLowerInvariant(), parameterValue));
#pragma warning restore CA1308

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
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] is not ' ' and not '\t')
                    return value[i..];
            }

            return [];
        }
    }
}
