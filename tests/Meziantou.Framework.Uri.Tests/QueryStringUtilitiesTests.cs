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
}
