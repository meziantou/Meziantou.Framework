#pragma warning disable CA1054 // URI-like parameters should not be strings
using System.Text.Encodings.Web;
using Microsoft.Extensions.Primitives;

namespace Meziantou.Framework;

/// <summary>Provides methods for parsing and manipulating query strings.</summary>
public static class QueryStringUtilities
{
    /// <summary>Append the given query key and value to the URI.</summary>
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

    /// <summary>Append the given query keys and values to the URI.</summary>
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

    /// <summary>Append the given query keys and values to the URI.</summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="queryString">A dictionary of query keys and values to append.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="queryString"/> is <see langword="null"/>.</exception>
    public static string AddQueryString(string uri, IEnumerable<(string Name, string? Value)> queryString)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return AddQueryString(uri, queryString.Select(tuple => KeyValuePair.Create(tuple.Name, tuple.Value)));
    }

    /// <summary>Append the given query keys and values to the URI.</summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="queryString">A dictionary of query keys and values to append.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="queryString"/> is <see langword="null"/>.</exception>
    public static string AddQueryString(string uri, IEnumerable<(string Name, StringValues Value)> queryString)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return AddQueryString(uri, queryString.Select(tuple => KeyValuePair.Create(tuple.Name, tuple.Value)));
    }

    /// <summary>Append the given query keys and values to the URI.</summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="queryString">A collection of query names and values to append.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="queryString"/> is <see langword="null"/>.</exception>
    public static string AddQueryString(string uri, IEnumerable<KeyValuePair<string, StringValues>> queryString)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(queryString);

        return AddQueryString(uri, queryString.SelectMany(kvp => kvp.Value, (kvp, v) => KeyValuePair.Create(kvp.Key, v)));
    }

    /// <summary>Append the given query keys and values to the URI.</summary>
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

    /// <summary>Replace the query string with the given query key and value.</summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="queryString">A collection of name value query pairs to set.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="queryString"/> is <see langword="null"/>.</exception>
    public static string SetQueryString(string uri, IEnumerable<KeyValuePair<string, StringValues>> queryString)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(queryString);

        return SetQueryString(uri, queryString.SelectMany(kvp => kvp.Value, (kvp, v) => KeyValuePair.Create(kvp.Key, v)));
    }

    /// <summary>Replace the query string with the given query key and value.</summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="queryString">A collection of name value query pairs to set.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="queryString"/> is <see langword="null"/>.</exception>
    public static string SetQueryString(string uri, IEnumerable<KeyValuePair<string, string?>> queryString)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(queryString);

        var anchorIndex = uri.IndexOf('#', StringComparison.Ordinal);
        var queryIndex = uri.IndexOf('?', StringComparison.Ordinal);

        var sb = new StringBuilder();
        if (queryIndex != -1)
        {
            sb.Append(uri[..queryIndex]);
        }
        else
        {
            if (anchorIndex != -1)
            {
                sb.Append(uri[..anchorIndex]);
            }
            else
            {
                sb.Append(uri);
            }
        }

        var hasQuery = false;
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

        if (anchorIndex != -1)
        {
            sb.Append(uri[anchorIndex..]);
        }

        return sb.ToString();
    }

    /// <summary>Append the given query key and value to the URI.</summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="queryString">A collection of name value query pairs to set.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="queryString"/> is <see langword="null"/>.</exception>
    public static string AddOrReplaceQueryString(string uri, IEnumerable<(string Name, string Value)> queryString)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(queryString);

        var parsed = ParseNullableQuery(GetQueryString(uri));
        if (parsed is null)
            return AddQueryString(uri, queryString);

        foreach (var (name, value) in queryString)
        {
            parsed[name] = value;
        }

        return SetQueryString(uri, parsed);
    }

    /// <summary>Append the given query key and value to the URI.</summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="queryString">A collection of name value query pairs to set.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="queryString"/> is <see langword="null"/>.</exception>
    public static string AddOrReplaceQueryString(string uri, IEnumerable<(string Name, StringValues Value)> queryString)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(queryString);

        var parsed = ParseNullableQuery(GetQueryString(uri));
        if (parsed is null)
            return AddQueryString(uri, queryString);

        foreach (var (name, value) in queryString)
        {
            parsed[name] = value;
        }

        return SetQueryString(uri, parsed);
    }

    /// <summary>Append the given query key and value to the URI.</summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="queryString">A collection of name value query pairs to set.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="queryString"/> is <see langword="null"/>.</exception>
    public static string AddOrReplaceQueryString(string uri, IEnumerable<KeyValuePair<string, StringValues>> queryString)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(queryString);

        var parsed = ParseNullableQuery(GetQueryString(uri));
        if (parsed is null)
            return AddQueryString(uri, queryString);

        foreach (var parameter in queryString)
        {
            parsed[parameter.Key] = parameter.Value;
        }

        return SetQueryString(uri, parsed);
    }

    /// <summary>Adds or replaces the given query key and value in the URI.</summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="name">The name of the query key.</param>
    /// <param name="value">The query value.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static string AddOrReplaceQueryString(string uri, string name, string? value)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(name);

        var parsed = ParseNullableQuery(GetQueryString(uri));
        if (parsed is null)
            return AddQueryString(uri, name, value);

        parsed[name] = value;
        return SetQueryString(uri, parsed);
    }

    /// <summary>Append the given query key and value to the URI.</summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="queryString">A collection of name value query pairs to set.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="queryString"/> is <see langword="null"/>.</exception>
    public static string AddOrReplaceQueryString(string uri, IEnumerable<KeyValuePair<string, string?>> queryString)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(queryString);

        var accumulator = new QueryStringParameterCollection();
        foreach (var parameter in queryString)
        {
            accumulator.Append(parameter.Key, parameter.Value);
        }

        return AddOrReplaceQueryString(uri, accumulator);
    }

    /// <summary>Removes the specified query parameter from the URI.</summary>
    /// <param name="uri">The base URI.</param>
    /// <param name="name">The name of the query key.</param>
    /// <returns>The combined result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static string RemoveQueryString(string uri, string name)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(name);

        return AddOrReplaceQueryString(uri, name, value: null);
    }

    /// <summary>Parse a query string into its component key and value parts.</summary>
    /// <param name="queryString">The raw query string value, with or without the leading '?'.</param>
    /// <returns>A collection of parsed keys and values.</returns>
    public static QueryStringParameterCollection ParseQuery(string? queryString)
    {
        var result = ParseNullableQuery(queryString);

        if (result is null)
            return new QueryStringParameterCollection();

        return result;
    }

    /// <summary>Parse a query string into its component key and value parts.</summary>
    /// <param name="queryString">The raw query string value, with or without the leading '?'.</param>
    /// <returns>A collection of parsed keys and values, null if there are no entries.</returns>
    public static QueryStringParameterCollection? ParseNullableQuery(string? queryString)
    {
        var result = new QueryStringParameterCollection();

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
                result.Append(
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
                    result.Append(queryString[scanIndex..delimiterIndex], "");
                }
            }

            scanIndex = delimiterIndex + 1;
        }

        if (result.IsEmpty)
        {
            return null;
        }

        return result;
    }

    private static string? GetQueryString(string uri)
    {
        var queryIndex = uri.IndexOf('?', StringComparison.Ordinal);
        var anchorIndex = uri.IndexOf('#', StringComparison.Ordinal);

        if (queryIndex == -1)
        {
            return null;
        }

        if (anchorIndex == -1)
        {
            return uri[(queryIndex + 1)..];
        }

        return uri[(queryIndex + 1)..anchorIndex];
    }
}
