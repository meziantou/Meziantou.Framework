using Meziantou.Framework;
using Meziantou.Framework.Fuzzing;
using Microsoft.Extensions.Primitives;

namespace UriFuzz;

public static partial class UriFuzzTargets
{
    private const int MaxQueryStringParameters = 8;

    /// <summary>
    /// Parses the query string of a URI, and adds, sets, replaces and removes parameters. Nothing throws, and
    /// <c>ParseQuery</c> agrees with <c>ParseNullableQuery</c>. Writing the parsed parameters and parsing them back gives the
    /// same parameters, and the parameters without '=' are written without '='. Parsing with a case-insensitive comparer
    /// groups the values of the names that differ only by case. Each operation gives the parameters it describes, leaves
    /// the part before the query and the fragment unchanged, and keeps the other parameters.
    /// </summary>
    /// <remarks>
    /// The parameters are written with <see cref="System.Text.Encodings.Web.UrlEncoder"/>, which replaces an unpaired
    /// surrogate with U+FFFD, so the expected parameters that are written get the same replacement.
    /// </remarks>
    private static void QueryStringTarget(ReadOnlySpan<byte> data)
    {
        var texts = ReadTexts(data, 1 + (2 * MaxQueryStringParameters));
        var uri = texts.Count > 0 ? texts[0] : "";
        var parameters = new List<(string Name, string Value)>();
        for (var i = 1; i + 1 < texts.Count; i += 2)
        {
            parameters.Add((texts[i], texts[i + 1]));
        }

        var (prefix, query, fragment) = SplitUri(uri);
        var parsed = QueryStringUtilities.ParseQuery(query, StringComparer.Ordinal);
        var nullable = QueryStringUtilities.ParseNullableQuery(query, StringComparer.Ordinal);
        FuzzAssert.Equal(parsed.IsEmpty, nullable is null, "ParseNullableQuery does not return null for a query without parameters");
        FuzzAssert.Equal(Format(parsed), Format(nullable ?? parsed), "ParseQuery and ParseNullableQuery disagree");
        FuzzAssert.Equal(parsed.Count, parsed.Select(parameter => parameter.Key).Distinct(StringComparer.Ordinal).Count(), "A name is listed twice");
        if (query is not null && !query.StartsWith('?', StringComparison.Ordinal))
        {
            FuzzAssert.Equal(Format(parsed), Format(QueryStringUtilities.ParseQuery("?" + query, StringComparer.Ordinal)), "A leading '?' changes the parameters");
        }

        // AddQueryString keeps the query of the URI as it is, and the other operations write all the parameters
        var original = Flatten(parsed);
        var written = Scalar(original);
        var rewritten = QueryStringUtilities.SetQueryString(uri, parsed);
        CheckUri(rewritten, prefix, fragment, Group(written), "Writing the parsed parameters");
        FuzzAssert.Equal(CountParametersWithoutValue(query), CountParametersWithoutValue(SplitUri(rewritten).Query), $"Writing the parsed parameters changes the parameters without '=' ({FuzzAssert.Format(rewritten)})");

        var caseInsensitive = QueryStringUtilities.ParseQuery(query, StringComparer.OrdinalIgnoreCase);
        FuzzAssert.Equal(Group(original, StringComparer.OrdinalIgnoreCase), Format(caseInsensitive), "Parsing with a case-insensitive comparer gives other parameters");
        foreach (var parameter in parsed)
        {
            FuzzAssert.Equal(Format(caseInsensitive[parameter.Key]), Format(caseInsensitive[parameter.Key.ToUpperInvariant()]), "A case-insensitive lookup depends on the case");
        }
        CheckUri(QueryStringUtilities.AddQueryString(uri, parameters.Select(parameter => (parameter.Name, (string?)parameter.Value))), prefix, fragment, Group([.. original, .. Scalar(parameters)]), "AddQueryString");
        CheckUri(QueryStringUtilities.SetQueryString(uri, parameters.Select(parameter => KeyValuePair.Create(parameter.Name, (string?)parameter.Value))), prefix, fragment, Group(Scalar(parameters)), "SetQueryString");

        if (parameters.Count > 0)
        {
            var (name, value) = parameters[0];
            CheckUri(QueryStringUtilities.AddQueryString(uri, name, value), prefix, fragment, Group([.. original, .. Scalar([(name, value)])]), "AddQueryString(name, value)");

            var others = original.Where(parameter => parameter.Name != name).ToList();
            CheckUri(QueryStringUtilities.RemoveQueryString(uri, name, StringComparer.Ordinal), prefix, fragment, Group(Scalar(others)), "RemoveQueryString");

            // A replaced parameter keeps its position
            others.Insert(original.TakeWhile(parameter => parameter.Name != name).Count(), (name, value));
            CheckUri(QueryStringUtilities.AddOrReplaceQueryString(uri, name, value, StringComparer.Ordinal), prefix, fragment, Group(Scalar(others)), "AddOrReplaceQueryString");
        }
    }

    private static void CheckUri(string uri, string prefix, string fragment, string expected, string operation)
    {
        var (actualPrefix, query, actualFragment) = SplitUri(uri);
        FuzzAssert.Equal(prefix, actualPrefix, $"{operation} changes the part before the query");
        FuzzAssert.Equal(fragment, actualFragment, $"{operation} changes the fragment");
        FuzzAssert.Equal(expected, Format(QueryStringUtilities.ParseQuery(query, StringComparer.Ordinal)), $"{operation} gives other parameters ({FuzzAssert.Format(uri)})");
    }

    // Same split as QueryStringUtilities: the fragment starts at the first '#', and the query at the first '?' before it
    private static (string Prefix, string? Query, string Fragment) SplitUri(string uri)
    {
        var anchorIndex = uri.IndexOf('#', StringComparison.Ordinal);
        var fragment = anchorIndex < 0 ? "" : uri[anchorIndex..];
        var beforeFragment = anchorIndex < 0 ? uri : uri[..anchorIndex];
        var queryIndex = beforeFragment.IndexOf('?', StringComparison.Ordinal);
        return queryIndex < 0 ? (beforeFragment, null, fragment) : (beforeFragment[..queryIndex], beforeFragment[(queryIndex + 1)..], fragment);
    }

    private static List<(string Name, string Value)> Flatten(IEnumerable<KeyValuePair<string, StringValues>> parameters)
        => [.. parameters.SelectMany(parameter => parameter.Value, (parameter, value) => (parameter.Key, value!))];

    private static List<(string Name, string Value)> Scalar(IEnumerable<(string Name, string Value)> parameters)
        => [.. parameters.Select(parameter => (ToScalarValueString(parameter.Name), ToScalarValueString(parameter.Value)))];

    /// <summary>Counts the non-empty parameters that have no '=', after the optional leading '?' that parsing skips.</summary>
    private static int CountParametersWithoutValue(string? query)
    {
        if (query is null)
            return 0;

        if (query.StartsWith('?', StringComparison.Ordinal))
        {
            query = query[1..];
        }

        return query.Split('&').Count(parameter => parameter.Length > 0 && !parameter.Contains('=', StringComparison.Ordinal));
    }

    /// <summary>Groups the values by name, in the order of the first occurrence of each name, as parsing does.</summary>
    private static string Group(IEnumerable<(string Name, string Value)> parameters, IEqualityComparer<string>? comparer = null)
    {
        var collection = new QueryStringParameterCollection(comparer);
        foreach (var (name, value) in parameters)
        {
            collection.Append(name, value);
        }

        return Format(collection);
    }

    private static string Format(StringValues values)
        => "[" + string.Join(", ", values.Select(FuzzAssert.Format)) + "]";

    private static string Format(QueryStringParameterCollection parameters)
        => string.Join(", ", parameters.Select(parameter => FuzzAssert.Format(parameter.Key) + ": [" + string.Join(", ", parameter.Value.Select(FuzzAssert.Format)) + "]"));

    private static IEnumerable<FuzzSeed> GetQueryStringSeeds()
    {
        yield return new FuzzSeed("empty", Array.Empty<byte>());
        yield return new FuzzSeed("parse", Encode([], "http://www.example.com/?a=1&b=2&a=3&c&=d&+e+=f+g&%41=%42#hash"));
        yield return new FuzzSeed("add", Encode([], "http://www.example.com/?a=old&extra=b#hash", "a", "1", "b", "2", "b", "3"));
        yield return new FuzzSeed("fragment", Encode([], "http://www.example.com/#/search?x=1", "x", "1"));
        yield return new FuzzSeed("no-value", Encode([], "http://www.example.com/?debug&a=1&debug=&Debug&a%20b&c+d#hash", "debug", "1"));
        yield return new FuzzSeed("case", Encode([], "http://www.example.com/?Tag=a&tag=b&TAG&\u0130=1&i=2", "TAG", "c"));
        yield return new FuzzSeed("special", Encode([], "?a b=c d&%=%&\u00E9=\u00E9", " a&b=", "=?#%+"));

        var literals = GetStringLiterals("QueryStringUtilitiesTests.cs").ToList();
        foreach (var literal in literals)
        {
            yield return new FuzzSeed("literal", Encode([], literal, "a", "1"));
        }
    }
}
