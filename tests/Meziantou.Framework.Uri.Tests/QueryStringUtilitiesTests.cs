using Microsoft.Extensions.Primitives;

namespace Meziantou.Framework.Uri.Tests;

public sealed class QueryStringUtilitiesTests
{
    [Fact]
    public void ReadmeExample_Usage()
    {
        #region QueryStringUtilities_Usage
        var query = QueryStringUtilities.ParseQuery("?author=meziantou&tag=dotnet&tag=csharp");
        Assert.Equal("meziantou", query["author"].ToString());
        Assert.Equal(["dotnet", "csharp"], query["tag"].ToArray());

        var uri = "https://www.meziantou.net/?author=meziantou";
        Assert.Equal("https://www.meziantou.net/?author=meziantou&name=value", QueryStringUtilities.AddQueryString(uri, "name", "value"));
        Assert.Equal("https://www.meziantou.net/?author=john", QueryStringUtilities.AddOrReplaceQueryString(uri, "author", "john"));

        // Replace the whole query string
        Assert.Equal("https://www.meziantou.net/?name=value", QueryStringUtilities.SetQueryString(uri, [KeyValuePair.Create("name", (string?)"value")]));

        // Remove a query string parameter
        Assert.Equal("https://www.meziantou.net/", QueryStringUtilities.RemoveQueryString(uri, "author"));
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
        var actual = QueryStringUtilities.AddOrReplaceQueryString(uri, [KeyValuePair.Create("a", new StringValues("1")), KeyValuePair.Create("b", new StringValues(["2", "3"]))]);
        Assert.Equal("http://www.example.com/?a=1&extra=b&b=2&b=3#hash", actual);
    }

    [Fact]
    public void RemoveQueryString()
    {
        var uri = "http://www.example.com/?a=1&b=2&a=3#hash";
        var actual = QueryStringUtilities.RemoveQueryString(uri, "a");
        Assert.Equal("http://www.example.com/?b=2#hash", actual);
    }

    [Fact]
    public void RemoveQueryString_QuestionMarkInFragment_DoesNotThrow()
    {
        var uri = "http://www.example.com/#/search?x=1";
        var actual = QueryStringUtilities.RemoveQueryString(uri, "x");
        Assert.Equal("http://www.example.com/#/search?x=1", actual);
    }

    [Fact]
    public void AddOrReplaceQueryString_QuestionMarkInFragment_AddsARealQuery()
    {
        var uri = "http://www.example.com/#/search?x=1";
        var actual = QueryStringUtilities.AddOrReplaceQueryString(uri, "a", "1");
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
        var collection = new QueryStringParameterCollection();
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
        var collection = new QueryStringParameterCollection();
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
        var collection = new QueryStringParameterCollection();
        collection.Append("a", "1");
        collection.Append("a", "2");
        collection.Set("a", new StringValues("3"));

        Assert.Equal<string?>(["3"], collection["a"].ToArray());
        Assert.Equal(1, collection.Count);
    }

    [Fact]
    public void ParameterCollection_RemoveAndClear()
    {
        var collection = new QueryStringParameterCollection();
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

        var result = QueryStringUtilities.ParseQuery(query);

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
        var query = QueryStringUtilities.ParseQuery("q=hello+world&path=%2Fdocs&tag=a&tag=b&debug");

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
        Assert.Null(QueryStringUtilities.ParseNullableQuery("?"));
        Assert.True(QueryStringUtilities.ParseQuery("?").IsEmpty);
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
        Assert.Equal("https://example.com/search?q=novels#results", QueryStringUtilities.AddOrReplaceQueryString(uri, "q", "novels"));
        Assert.Equal("https://example.com/search#results", QueryStringUtilities.AddOrReplaceQueryString(uri, "q", null));

        // The parameters that are kept are written again
        Assert.Equal("https://example.com/?q=a%20b&debug=&page=2", QueryStringUtilities.AddOrReplaceQueryString("https://example.com/?q=a+b&debug", "page", "2"));
        #endregion
    }

    [Fact]
    public void Documentation_ParameterCollection()
    {
        #region QueryStringParameterCollection_Edit
        var uri = "https://example.com/search?q=books&page=3&utm_source=newsletter";

        var query = QueryStringUtilities.ParseQuery(new System.Uri(uri).Query);
        query.Remove("utm_source");
        query.Set("page", "1");
        query.Append("tag", "fiction");
        query.Append("tag", "history");

        Assert.Equal(
            "https://example.com/search?q=books&page=1&tag=fiction&tag=history",
            QueryStringUtilities.SetQueryString(uri, query));
        #endregion
    }
}
