// https://github.com/dotnet/aspnetcore/blob/e43027e506f5b004e3c3609f588c3e31f41aebfc/src/Http/WebUtilities/src/QueryHelpers.cs
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Primitives;

namespace Meziantou.AspNetCore.Components.Internals;

/// <summary>
/// Provides methods for parsing and manipulating query strings.
/// </summary>
internal static class QueryHelpers
{
    /// <summary>
    /// Append the given query key and value to the URI.
    /// </summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="name">The name of the query key.</param>
    /// <param name="value">The query value.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string AddQueryString(string uri, string name, string? value)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(name);

        return AddQueryString(uri, [new KeyValuePair<string, string?>(name, value)]);
    }

    /// <summary>
    /// Append the given query keys and values to the URI.
    /// </summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="queryString">A dictionary of query keys and values to append.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="queryString"/> is <see langword="null"/>.</exception>
    public static string AddQueryString(string uri, IDictionary<string, string?> queryString)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(queryString);

        return AddQueryString(uri, (IEnumerable<KeyValuePair<string, string?>>)queryString);
    }

    /// <summary>
    /// Append the given query keys and values to the URI.
    /// </summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="queryString">A collection of query names and values to append.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="queryString"/> is <see langword="null"/>.</exception>
    public static string AddQueryString(string uri, IEnumerable<KeyValuePair<string, StringValues>> queryString)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(queryString);

        return AddQueryString(uri, queryString.SelectMany(kvp => kvp.Value, (kvp, v) => KeyValuePair.Create<string, string?>(kvp.Key, v)));
    }

    /// <summary>
    /// Append the given query keys and values to the URI.
    /// </summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="queryString">A collection of name value query pairs to append.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="queryString"/> is <see langword="null"/>.</exception>
    public static string AddQueryString(string uri, IEnumerable<KeyValuePair<string, string?>> queryString)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(queryString);

        var anchorIndex = uri.IndexOf('#', StringComparison.Ordinal);
        var uriToBeAppended = uri;
        var anchorText = "";
        // If there is an anchor, then the query string must be inserted before its first occurrence.
        if (anchorIndex != -1)
        {
            anchorText = uri[anchorIndex..];
            uriToBeAppended = uri[..anchorIndex];
        }

        var hasQuery = uriToBeAppended.Contains('?', StringComparison.Ordinal);

        var sb = new StringBuilder();
        sb.Append(uriToBeAppended);
        foreach (var parameter in queryString)
        {
            if (parameter.Value is null)
            {
                continue;
            }

            sb.Append(hasQuery ? '&' : '?');
            sb.Append(UrlEncoder.Default.Encode(parameter.Key));
            sb.Append('=');
            sb.Append(UrlEncoder.Default.Encode(parameter.Value));
            hasQuery = true;
        }

        sb.Append(anchorText);
        return sb.ToString();
    }

    /// <summary>
    /// Parse a query string into its component key and value parts.
    /// </summary>
    /// <param name="queryString">The raw query string value, with or without the leading '?'.</param>
    /// <returns>A collection of parsed keys and values.</returns>
    public static Dictionary<string, StringValues> ParseQuery(string? queryString)
    {
        var result = ParseNullableQuery(queryString);

        if (result is null)
        {
            return new Dictionary<string, StringValues>(StringComparer.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// Parse a query string into its component key and value parts.
    /// </summary>
    /// <param name="queryString">The raw query string value, with or without the leading '?'.</param>
    /// <returns>A collection of parsed keys and values, null if there are no entries.</returns>
    public static Dictionary<string, StringValues>? ParseNullableQuery(string? queryString)
    {
        var accumulator = new KeyValueAccumulator();

        if (string.IsNullOrEmpty(queryString) || queryString == "?")
        {
            return null;
        }

        var scanIndex = 0;
        if (queryString[0] == '?')
        {
            scanIndex = 1;
        }

        var textLength = queryString.Length;
        var equalIndex = queryString.IndexOf('=', StringComparison.Ordinal);
        if (equalIndex == -1)
        {
            equalIndex = textLength;
        }

        while (scanIndex < textLength)
        {
            var delimiterIndex = queryString.IndexOf('&', scanIndex);
            if (delimiterIndex == -1)
            {
                delimiterIndex = textLength;
            }

            if (equalIndex < delimiterIndex)
            {
                while (scanIndex != equalIndex && char.IsWhiteSpace(queryString[scanIndex]))
                {
                    ++scanIndex;
                }

                var name = queryString[scanIndex..equalIndex];
                var value = queryString.Substring(equalIndex + 1, delimiterIndex - equalIndex - 1);
                accumulator.Append(
                    Uri.UnescapeDataString(name.Replace('+', ' ')),
                    Uri.UnescapeDataString(value.Replace('+', ' ')));
                equalIndex = queryString.IndexOf('=', delimiterIndex);
                if (equalIndex == -1)
                {
                    equalIndex = textLength;
                }
            }
            else
            {
                if (delimiterIndex > scanIndex)
                {
                    accumulator.Append(queryString[scanIndex..delimiterIndex], string.Empty);
                }
            }

            scanIndex = delimiterIndex + 1;
        }

        if (!accumulator.HasValues)
        {
            return null;
        }

        return accumulator.GetResults();
    }
}
