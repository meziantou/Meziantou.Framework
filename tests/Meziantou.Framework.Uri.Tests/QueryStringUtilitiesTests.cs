using Microsoft.Extensions.Primitives;

namespace Meziantou.Framework.Uri.Tests;

public sealed class QueryStringUtilitiesTests
{
    [Fact]
    public void ReadmeExample_Usage()
    {
        #region QueryStringUtilities_Usage
        var query = QueryStringUtilities.ParseQuery("?author=meziantou&tag=dotnet&tag=csharp", StringComparer.Ordinal);
        Assert.Equal("meziantou", query["author"].ToString());
        Assert.Equal(["dotnet", "csharp"], query["tag"].ToArray());

        var uri = "https://www.meziantou.net/?author=meziantou";
        Assert.Equal("https://www.meziantou.net/?author=meziantou&name=value", QueryStringUtilities.AddQueryString(uri, "name", "value"));
        Assert.Equal("https://www.meziantou.net/?author=john", QueryStringUtilities.AddOrReplaceQueryString(uri, "author", "john", StringComparer.Ordinal));

        // Replace the whole query string
        Assert.Equal("https://www.meziantou.net/?name=value", QueryStringUtilities.SetQueryString(uri, [KeyValuePair.Create("name", (string?)"value")]));

        // Remove a query string parameter
        Assert.Equal("https://www.meziantou.net/", QueryStringUtilities.RemoveQueryString(uri, "author", StringComparer.Ordinal));
        #endregion
    }

    [Fact]
    public void SetQueryString_Append()
    {
        var uri = "http://www.example.com/";
        var actual = QueryStringUtilities.SetQueryString(uri, [KeyValuePair.Create("a", new StringValues("1")), KeyValuePair.Create("b", new StringValues(["2", "3"]))]);
        Assert.Equal("http://www.example.com/?a=1&b=2&b=3", actual);
    }

    [Fact]
    public void SetQueryString_PreserveHash()
    {
        var uri = "http://www.example.com/#hash";
        var actual = QueryStringUtilities.SetQueryString(uri, [KeyValuePair.Create("a", new StringValues("1")), KeyValuePair.Create("b", new StringValues(["2", "3"]))]);
        Assert.Equal("http://www.example.com/?a=1&b=2&b=3#hash", actual);
    }

    [Fact]
    public void SetQueryString_OverrideExtraParameters()
    {
        var uri = "http://www.example.com/?a=old&extra#hash";
        var actual = QueryStringUtilities.SetQueryString(uri, [KeyValuePair.Create("a", new StringValues("1")), KeyValuePair.Create("b", new StringValues(["2", "3"]))]);
        Assert.Equal("http://www.example.com/?a=1&b=2&b=3#hash", actual);
    }

    [Fact]
    public void AddOrReplaceQueryString()
    {
        var uri = "http://www.example.com/?a=old&extra=b#hash";
        var actual = QueryStringUtilities.AddOrReplaceQueryString(uri, [KeyValuePair.Create("a", new StringValues("1")), KeyValuePair.Create("b", new StringValues(["2", "3"]))], StringComparer.Ordinal);
        Assert.Equal("http://www.example.com/?a=1&extra=b&b=2&b=3#hash", actual);
    }

    [Fact]
    public void RemoveQueryString()
    {
        var uri = "http://www.example.com/?a=1&b=2&a=3#hash";
        var actual = QueryStringUtilities.RemoveQueryString(uri, "a", StringComparer.Ordinal);
        Assert.Equal("http://www.example.com/?b=2#hash", actual);
    }

    [Fact]
    public void RemoveQueryString_QuestionMarkInFragment_DoesNotThrow()
    {
        var uri = "http://www.example.com/#/search?x=1";
        var actual = QueryStringUtilities.RemoveQueryString(uri, "x", StringComparer.Ordinal);
        Assert.Equal("http://www.example.com/#/search?x=1", actual);
    }

    [Fact]
    public void AddOrReplaceQueryString_QuestionMarkInFragment_AddsARealQuery()
    {
        var uri = "http://www.example.com/#/search?x=1";
        var actual = QueryStringUtilities.AddOrReplaceQueryString(uri, "a", "1", StringComparer.Ordinal);
        Assert.Equal("http://www.example.com/?a=1#/search?x=1", actual);
    }

    [Fact]
    public void SetQueryString_QuestionMarkInFragment_DoesNotDuplicateTheFragment()
    {
        var uri = "http://www.example.com/#/search?x=1";
        var actual = QueryStringUtilities.SetQueryString(uri, [KeyValuePair.Create<string, string?>("a", "1")]);
        Assert.Equal("http://www.example.com/?a=1#/search?x=1", actual);
    }

    [Fact]
    public void SetQueryString_QueryAndFragmentContainingAQuestionMark()
    {
        var uri = "http://www.example.com/?old=1#/search?x=1";
        var actual = QueryStringUtilities.SetQueryString(uri, [KeyValuePair.Create<string, string?>("a", "1")]);
        Assert.Equal("http://www.example.com/?a=1#/search?x=1", actual);
    }

    [Fact]
    public void AddQueryString_QuestionMarkInFragment_AddsARealQuery()
    {
        var uri = "http://www.example.com/#/search?x=1";
        var actual = QueryStringUtilities.AddQueryString(uri, "a", "1");
        Assert.Equal("http://www.example.com/?a=1#/search?x=1", actual);
    }

    [Fact]
    public void AddQueryString_ExistingQueryAndFragmentContainingAQuestionMark()
    {
        var uri = "http://www.example.com/?old=1#/search?x=1";
        var actual = QueryStringUtilities.AddQueryString(uri, "a", "1");
        Assert.Equal("http://www.example.com/?old=1&a=1#/search?x=1", actual);
    }

    [Fact]
    public void ParameterCollection_AppendAccumulatesValuesInOrder()
    {
        var collection = new QueryStringParameterCollection(StringComparer.Ordinal);
        collection.Append("a", "1");
        collection.Append("b", "2");
        collection.Append("a", "3");
        collection.Append("a", new StringValues(["4", "5"]));

        Assert.Equal(2, collection.Count);
        Assert.Equal<string?>(["1", "3", "4", "5"], collection["a"].ToArray());
        Assert.Equal<string?>(["2"], collection["b"].ToArray());
        Assert.Equal<string>(["a", "b"], collection.Select(kvp => kvp.Key).ToArray());
    }

    [Fact]
    public void ParameterCollection_ReadingBetweenAppendsKeepsAccumulating()
    {
        var collection = new QueryStringParameterCollection(StringComparer.Ordinal);
        collection.Append("a", "1");
        Assert.Equal<string?>(["1"], collection["a"].ToArray());

        collection.Append("a", "2");
        Assert.Equal<string?>(["1", "2"], collection["a"].ToArray());

        collection.Append("a", "3");
        Assert.Equal<string?>(["1", "2", "3"], collection["a"].ToArray());
    }

    [Fact]
    public void ParameterCollection_SetReplacesEveryAppendedValue()
    {
        var collection = new QueryStringParameterCollection(StringComparer.Ordinal);
        collection.Append("a", "1");
        collection.Append("a", "2");
        collection.Set("a", new StringValues("3"));

        Assert.Equal<string?>(["3"], collection["a"].ToArray());
        Assert.Equal(1, collection.Count);
    }

    [Fact]
    public void ParameterCollection_RemoveAndClear()
    {
        var collection = new QueryStringParameterCollection(StringComparer.Ordinal);
        collection.Append("a", "1");
        collection.Append("b", "2");

        Assert.True(collection.Remove("a"));
        Assert.False(collection.Remove("a"));
        Assert.Empty(collection["a"].ToArray());
        Assert.Equal(1, collection.Count);

        collection.Clear();
        Assert.True(collection.IsEmpty);
    }

    [Fact]
    public void ParseQuery_ManyValuesForTheSameName()
    {
        const int Count = 20_000;
        var query = string.Join("&", Enumerable.Range(0, Count).Select(i => "a=" + i.ToString(CultureInfo.InvariantCulture)));

        var result = QueryStringUtilities.ParseQuery(query, StringComparer.Ordinal);

        Assert.Equal(1, result.Count);
        Assert.HasCount(Count, result["a"].ToArray());
        Assert.Equal("0", result["a"][0]);
        Assert.Equal((Count - 1).ToString(CultureInfo.InvariantCulture), result["a"][Count - 1]);
    }

    [Fact]
    public void AddQueryString_NullValue_AppendsNothing()
    {
        Assert.Equal("http://www.example.com/", QueryStringUtilities.AddQueryString("http://www.example.com/", "a", value: null));
    }

    [Fact]
    public void AddQueryString_NullTupleCollection_ReturnsTheUriUnchanged()
    {
        Assert.Equal("http://www.example.com/", QueryStringUtilities.AddQueryString("http://www.example.com/", (IEnumerable<(string Name, string? Value)>?)null));
    }

    [Fact]
    public void AddQueryString_NullCollection_ThrowsForTheDocumentedParameter()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => QueryStringUtilities.AddQueryString("http://www.example.com/", (IEnumerable<(string Name, StringValues Value)>)null!));

        Assert.Equal("queryString", exception.ParamName);
    }

    [Fact]
    public void Documentation_ParseQuery()
    {
        #region QueryStringUtilities_ParseQuery
        // The leading "?" is optional, names and values are decoded
        var query = QueryStringUtilities.ParseQuery("q=hello+world&path=%2Fdocs&tag=a&tag=b&debug", StringComparer.Ordinal);

        Assert.Equal("hello world", query["q"].ToString());
        Assert.Equal("/docs", query["path"].ToString());
        Assert.Equal(["a", "b"], query["tag"].ToArray());

        // A parameter without "=" has an empty value
        Assert.Equal("", query["debug"].ToString());

        // A missing parameter has no value
        Assert.True(StringValues.IsNullOrEmpty(query["missing"]));

        // Parameters are enumerated in the order of the query string
        Assert.Equal(["q", "path", "tag", "debug"], query.Select(parameter => parameter.Key));

        // ParseNullableQuery returns null instead of an empty collection
        Assert.Null(QueryStringUtilities.ParseNullableQuery("?", StringComparer.Ordinal));
        Assert.True(QueryStringUtilities.ParseQuery("?", StringComparer.Ordinal).IsEmpty);
        #endregion
    }

    [Fact]
    public void Documentation_EditQuery()
    {
        #region QueryStringUtilities_EditQuery
        var uri = "https://example.com/search?q=books#results";

        // Values are encoded, and the fragment stays at the end
        Assert.Equal(
            "https://example.com/search?q=books&author=Jane%20Doe#results",
            QueryStringUtilities.AddQueryString(uri, "author", "Jane Doe"));

        // Several parameters at once, a name with several values
        Assert.Equal(
            "https://example.com/search?q=books&tag=a&tag=b&page=2#results",
            QueryStringUtilities.AddQueryString(uri, [("tag", new StringValues(["a", "b"])), ("page", new StringValues("2"))]));

        // A null value adds nothing
        Assert.Equal(uri, QueryStringUtilities.AddQueryString(uri, "author", null));

        // AddOrReplaceQueryString replaces every value of the name, and a null value removes it
        Assert.Equal("https://example.com/search?q=novels#results", QueryStringUtilities.AddOrReplaceQueryString(uri, "q", "novels", StringComparer.Ordinal));
        Assert.Equal("https://example.com/search#results", QueryStringUtilities.AddOrReplaceQueryString(uri, "q", null, StringComparer.Ordinal));

        // The parameters that are kept are written again, and a parameter without "=" keeps this form
        Assert.Equal("https://example.com/?q=a%20b&debug&page=2", QueryStringUtilities.AddOrReplaceQueryString("https://example.com/?q=a+b&debug", "page", "2", StringComparer.Ordinal));
        #endregion
    }

    [Fact]
    public void Documentation_ParameterCollection()
    {
        #region QueryStringParameterCollection_Edit
        var uri = "https://example.com/search?q=books&page=3&utm_source=newsletter";

        var query = QueryStringUtilities.ParseQuery(new System.Uri(uri).Query, StringComparer.Ordinal);
        query.Remove("utm_source");
        query.Set("page", "1");
        query.Append("tag", "fiction");
        query.Append("tag", "history");

        Assert.Equal(
            "https://example.com/search?q=books&page=1&tag=fiction&tag=history",
            QueryStringUtilities.SetQueryString(uri, query));
        #endregion
    }

    [Fact]
    public void Documentation_CaseInsensitiveNames()
    {
        #region QueryStringUtilities_CaseInsensitiveNames
        var query = QueryStringUtilities.ParseQuery("?Tag=a&tag=b", StringComparer.OrdinalIgnoreCase);

        // The values of "Tag" and "tag" are grouped under the first name
        Assert.Equal(["a", "b"], query["TAG"].ToArray());
        Assert.Equal(["Tag"], query.Select(parameter => parameter.Key));

        // A replaced parameter keeps the name it has in the URL
        var uri = "https://example.com/?Page=3&Debug";
        Assert.Equal("https://example.com/?Page=1&Debug", QueryStringUtilities.AddOrReplaceQueryString(uri, "page", "1", StringComparer.OrdinalIgnoreCase));
        Assert.Equal("https://example.com/?Page=3", QueryStringUtilities.RemoveQueryString(uri, "debug", StringComparer.OrdinalIgnoreCase));
        #endregion
    }

    [Theory]
    [InlineData("http://example.com/?debug", "http://example.com/?debug")]
    [InlineData("http://example.com/?debug=", "http://example.com/?debug=")]
    [InlineData("http://example.com/?a=1&debug&b=2#hash", "http://example.com/?a=1&debug&b=2#hash")]
    [InlineData("http://example.com/?debug&debug=&debug", "http://example.com/?debug&debug=&debug")]
    [InlineData("http://example.com/?a%20b", "http://example.com/?a%20b")]
    [InlineData("http://example.com/?a+b", "http://example.com/?a%20b")]
    public void SetQueryString_ParsedQuery_KeepsParametersWithoutValue(string uri, string expected)
    {
        var query = QueryStringUtilities.ParseQuery(new System.Uri(uri).Query, StringComparer.Ordinal);

        Assert.Equal(expected, QueryStringUtilities.SetQueryString(uri, query));
    }

    [Fact]
    public void ParseQuery_ParameterWithoutValue_DecodesTheName()
    {
        var query = QueryStringUtilities.ParseQuery("?a%20b&c+d", StringComparer.Ordinal);

        Assert.Equal(["a b", "c d"], query.Select(parameter => parameter.Key));
    }

    [Fact]
    public void ParameterCollection_ParameterWithoutValue_AppendedValueHasEqualSign()
    {
        var query = QueryStringUtilities.ParseQuery("?debug", StringComparer.Ordinal);
        query.Append("debug", "1");

        Assert.Equal("http://example.com/?debug&debug=1", QueryStringUtilities.SetQueryString("http://example.com/", query));
    }

    [Fact]
    public void ParameterCollection_ParameterWithoutValue_SetValueHasEqualSign()
    {
        var query = QueryStringUtilities.ParseQuery("?debug&a=1", StringComparer.Ordinal);
        query["debug"] = "";

        Assert.Equal("http://example.com/?debug=&a=1", QueryStringUtilities.SetQueryString("http://example.com/", query));
    }

    [Fact]
    public void AddQueryString_ParsedQuery_KeepsParametersWithoutValue()
    {
        var query = QueryStringUtilities.ParseQuery("?debug&a=1", StringComparer.Ordinal);

        Assert.Equal("http://example.com/?x=1&debug&a=1#hash", QueryStringUtilities.AddQueryString("http://example.com/?x=1#hash", query));
    }

    [Fact]
    public void RemoveQueryString_KeepsOtherParametersWithoutValue()
    {
        Assert.Equal("http://example.com/?debug", QueryStringUtilities.RemoveQueryString("http://example.com/?debug&a=1", "a", StringComparer.Ordinal));
    }

    [Fact]
    public void ParameterCollection_NullComparer_IsCaseSensitive()
    {
        var query = new QueryStringParameterCollection(comparer: null);
        query.Append("a", "1");
        query.Append("A", "2");

        Assert.Same(StringComparer.Ordinal, query.Comparer);
        Assert.Equal(2, query.Count);
        Assert.False(query.Remove("B"));
    }

#pragma warning disable MA0002 // The overloads without a comparer are the ones under test
    [Fact]
    public void OverloadsWithoutComparer_CompareNamesWithExactCase()
    {
        var uri = "http://example.com/?A=1&debug";

        Assert.Same(StringComparer.Ordinal, new QueryStringParameterCollection().Comparer);
        Assert.Equal(["A", "debug"], QueryStringUtilities.ParseQuery(uri[19..]).Select(parameter => parameter.Key));
        Assert.Equal(["A", "debug"], QueryStringUtilities.ParseNullableQuery(uri[19..])!.Select(parameter => parameter.Key));
        Assert.Equal(uri, QueryStringUtilities.RemoveQueryString(uri, "a"));
        Assert.Equal("http://example.com/?A=1&debug&a=2", QueryStringUtilities.AddOrReplaceQueryString(uri, "a", "2"));
        Assert.Equal("http://example.com/?A=1&debug&a=2", QueryStringUtilities.AddOrReplaceQueryString(uri, [("a", (string?)"2")]));
        Assert.Equal("http://example.com/?A=1&debug&a=2", QueryStringUtilities.AddOrReplaceQueryString(uri, [("a", new StringValues("2"))]));
        Assert.Equal("http://example.com/?A=1&debug&a=2", QueryStringUtilities.AddOrReplaceQueryString(uri, [KeyValuePair.Create("a", new StringValues("2"))]));
        Assert.Equal("http://example.com/?A=1&debug&a=2", QueryStringUtilities.AddOrReplaceQueryString(uri, [KeyValuePair.Create("a", (string?)"2")]));
    }
#pragma warning restore MA0002

    [Fact]
    public void ParameterCollection_CaseInsensitiveComparer()
    {
        var query = new QueryStringParameterCollection(StringComparer.OrdinalIgnoreCase);
        query.Append("a", "1");
        query.Append("A", "2");
        query.Set("A", new StringValues(["3", "4"]));

        Assert.Equal(1, query.Count);
        Assert.Equal(["3", "4"], query.Get("a").ToArray());
        Assert.True(query.Remove("A"));
        Assert.True(query.IsEmpty);
    }

    [Fact]
    public void ParseQuery_CaseInsensitive_EmptyQueryKeepsTheComparer()
    {
        var query = QueryStringUtilities.ParseQuery("", StringComparer.OrdinalIgnoreCase);
        query.Append("a", "1");

        Assert.Equal("1", query["A"].ToString());
    }

    [Fact]
    public void AddOrReplaceQueryString_CaseInsensitive_Overloads()
    {
        var uri = "http://example.com/?A=1&b=2";
        var comparer = StringComparer.OrdinalIgnoreCase;

        Assert.Equal("http://example.com/?A=3&b=2", QueryStringUtilities.AddOrReplaceQueryString(uri, [("a", (string?)"3")], comparer));
        Assert.Equal("http://example.com/?A=3&A=4&b=2", QueryStringUtilities.AddOrReplaceQueryString(uri, [("a", new StringValues(["3", "4"]))], comparer));
        Assert.Equal("http://example.com/?A=3&b=2", QueryStringUtilities.AddOrReplaceQueryString(uri, [KeyValuePair.Create("a", new StringValues("3"))], comparer));
        Assert.Equal("http://example.com/?A=3&A=4&b=2", QueryStringUtilities.AddOrReplaceQueryString(uri, [KeyValuePair.Create("a", (string?)"3"), KeyValuePair.Create("A", (string?)"4")], comparer));

        // With an ordinal comparer, the names are compared with the exact case
        Assert.Equal("http://example.com/?A=1&b=2&a=3", QueryStringUtilities.AddOrReplaceQueryString(uri, "a", "3", StringComparer.Ordinal));
    }
}
