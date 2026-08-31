using Microsoft.Extensions.Primitives;

namespace Meziantou.Framework.Uri.Tests;

public sealed class QueryStringUtilitiesTests
{
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
}