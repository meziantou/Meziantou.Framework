using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Urls;

public sealed class UrlSearchParamsTests
{
    [Fact]
    public void Constructor_Empty()
    {
        var searchParams = new UrlSearchParams();

        Assert.Equal(0u, searchParams.Size);
        Assert.Equal("", searchParams.ToString());
        Assert.Empty(searchParams);
    }

    [Fact]
    public void Constructor_String()
    {
        var searchParams = new UrlSearchParams("?a=1&b=2&a=3&&c");

        Assert.Equal(4u, searchParams.Size);
        Assert.Equal(["1", "3"], searchParams.GetAll("a"));
        Assert.Equal("2", searchParams.Get("b"));
        Assert.Equal("", searchParams.Get("c"));
        Assert.Equal("a=1&b=2&a=3&c=", searchParams.ToString());
    }

    [Fact]
    public void Constructor_String_OnlyStripsOneLeadingQuestionMark()
    {
        var searchParams = new UrlSearchParams("??a=b");

        Assert.Equal("b", searchParams.Get("?a"));
        Assert.Null(searchParams.Get("a"));
    }

    [Fact]
    public void Constructor_String_DecodesPlusAndPercentEncoding()
    {
        var searchParams = new UrlSearchParams("a+b=c+d&e%20f=%C3%A9%2B&g=%zz");

        Assert.Equal("c d", searchParams.Get("a b"));
        Assert.Equal("\U000000E9+", searchParams.Get("e f"));
        Assert.Equal("%zz", searchParams.Get("g"));
    }

    [Fact]
    public void Constructor_String_ReplacesLoneSurrogates()
    {
        var searchParams = new UrlSearchParams("a=" + (char)0xD800);

        Assert.Equal("\U0000FFFD", searchParams.Get("a"));
        Assert.Equal("a=%EF%BF%BD", searchParams.ToString());
    }

    [Fact]
    public void Constructor_Sequence()
    {
        var searchParams = new UrlSearchParams([["a", "1"], ["b", "2"], ["a", "3"]]);

        Assert.Equal(["1", "3"], searchParams.GetAll("a"));
        Assert.Equal("a=1&b=2&a=3", searchParams.ToString());
    }

    [Fact]
    public void Constructor_Sequence_InvalidPairLength_Throws()
    {
        Assert.Throws<TypeErrorException>(() => new UrlSearchParams([["a"]]));
        Assert.Throws<TypeErrorException>(() => new UrlSearchParams([["a", "b", "c"]]));
    }

    [Fact]
    public void Constructor_Record()
    {
        var searchParams = new UrlSearchParams(new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "1", ["b"] = "2" });

        Assert.Equal("a=1&b=2", searchParams.ToString());
    }

    [Fact]
    public void Constructor_CopiesAnotherInstance()
    {
        var source = new UrlSearchParams("a=1&b=2");
        var copy = new UrlSearchParams(source);
        source.Append("c", "3");

        Assert.Equal("a=1&b=2", copy.ToString());
    }

    [Fact]
    public void Constructor_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new UrlSearchParams((string)null!));
        Assert.Throws<ArgumentNullException>(() => new UrlSearchParams((IEnumerable<IReadOnlyList<string>>)null!));
        Assert.Throws<ArgumentNullException>(() => new UrlSearchParams((IEnumerable<KeyValuePair<string, string>>)null!));
    }

    [Fact]
    public void Append_KeepsDuplicates()
    {
        var searchParams = new UrlSearchParams();
        searchParams.Append("a", "b");
        searchParams.Append("a", "c");

        Assert.Equal(2u, searchParams.Size);
        Assert.Equal("b", searchParams.Get("a"));
        Assert.Equal(["b", "c"], searchParams.GetAll("a"));
    }

    [Fact]
    public void Delete_ByName()
    {
        var searchParams = new UrlSearchParams("a=1&b=2&a=3");
        searchParams.Delete("a");

        Assert.Equal("b=2", searchParams.ToString());
    }

    [Fact]
    public void Delete_ByNameAndValue()
    {
        var searchParams = new UrlSearchParams("a=1&b=2&a=3&a=1");
        searchParams.Delete("a", "1");

        Assert.Equal("b=2&a=3", searchParams.ToString());
    }

    [Fact]
    public void Get_Missing_ReturnsNull()
    {
        var searchParams = new UrlSearchParams("a=1");

        Assert.Null(searchParams.Get("b"));
        Assert.Empty(searchParams.GetAll("b"));
    }

    [Fact]
    public void Has_ByNameAndValue()
    {
        var searchParams = new UrlSearchParams("a=1&a=2");

        Assert.True(searchParams.Has("a"));
        Assert.True(searchParams.Has("a", "2"));
        Assert.False(searchParams.Has("a", "3"));
        Assert.False(searchParams.Has("b"));
    }

    [Fact]
    public void Set_ReplacesFirstAndRemovesOthers()
    {
        var searchParams = new UrlSearchParams("a=1&b=2&a=3&c=4");
        searchParams.Set("a", "5");

        Assert.Equal("a=5&b=2&c=4", searchParams.ToString());
    }

    [Fact]
    public void Set_Missing_Appends()
    {
        var searchParams = new UrlSearchParams("a=1");
        searchParams.Set("b", "2");

        Assert.Equal("a=1&b=2", searchParams.ToString());
    }

    [Fact]
    public void Sort_IsStable()
    {
        var searchParams = new UrlSearchParams("z=b&a=b&z=a&a=a");
        searchParams.Sort();

        Assert.Equal("a=b&a=a&z=b&z=a", searchParams.ToString());
    }

    [Fact]
    public void Sort_ComparesCodeUnits()
    {
        // U+1F308 is encoded as the surrogate pair D83C DF08, which sorts before U+FB03
        var searchParams = new UrlSearchParams();
        searchParams.Append("\U0000FB03", "1");
        searchParams.Append("\U0001F308", "2");
        searchParams.Sort();

        Assert.Equal(["\U0001F308", "\U0000FB03"], searchParams.Select(pair => pair.Key));
    }

    [Fact]
    public void ToString_EncodesWithApplicationXWwwFormUrlEncodedPercentEncodeSet()
    {
        var searchParams = new UrlSearchParams();
        searchParams.Append("a b", "c+d&e=f");
        searchParams.Append("*-._", "~!\U000000E9");

        Assert.Equal("a+b=c%2Bd%26e%3Df&*-._=%7E%21%C3%A9", searchParams.ToString());
    }

    [Fact]
    public void Enumerate_ReturnsPairsInOrder()
    {
        var searchParams = new UrlSearchParams("b=1&a=2&b=3");

        Assert.Equal(
            [new("b", "1"), new("a", "2"), new("b", "3")],
            searchParams.ToList());
    }

    [Fact]
    public void Enumerate_IsLive()
    {
        var searchParams = new UrlSearchParams("a=1&b=2&c=3");
        var names = new List<string>();
        foreach (var (name, _) in searchParams)
        {
            names.Add(name);
            if (name == "a")
            {
                searchParams.Delete("a");
            }
        }

        // Deleting the current pair shifts the list, so "b" is skipped
        Assert.Equal(["a", "c"], names);
    }

    [Fact]
    public void Url_SearchParams_ReflectsQuery()
    {
        var url = new Url("https://example.com/?a=1&b=2");

        Assert.Same(url.SearchParams, url.SearchParams);
        Assert.Equal("1", url.SearchParams.Get("a"));
        Assert.Equal(2u, url.SearchParams.Size);
    }

    [Fact]
    public void Url_SearchParams_UpdatesUrl()
    {
        var url = new Url("https://example.com/path?a=1#hash");
        url.SearchParams.Append("b", "c d");

        Assert.Equal("?a=1&b=c+d", url.Search);
        Assert.Equal("https://example.com/path?a=1&b=c+d#hash", url.Href);
    }

    [Fact]
    public void Url_SearchParams_EmptyListRemovesQuery()
    {
        var url = new Url("https://example.com/?a=1");
        url.SearchParams.Delete("a");

        Assert.Equal("", url.Search);
        Assert.Equal("https://example.com/", url.Href);
    }

    [Fact]
    public void Url_SetSearch_UpdatesSearchParams()
    {
        var url = new Url("https://example.com/?a=1");
        var searchParams = url.SearchParams;
        url.Search = "?b=2&c=3";

        Assert.Same(searchParams, url.SearchParams);
        Assert.Null(searchParams.Get("a"));
        Assert.Equal("b=2&c=3", searchParams.ToString());

        url.Search = "";
        Assert.Equal(0u, searchParams.Size);
    }

    [Fact]
    public void Url_SetHref_UpdatesSearchParams()
    {
        var url = new Url("https://example.com/?a=1");
        url.Href = "https://example.org/?x=y";

        Assert.Equal("y", url.SearchParams.Get("x"));
        Assert.False(url.SearchParams.Has("a"));
    }

    [Fact]
    public void Url_SearchParams_SerializationDiffersFromParsedQuery()
    {
        // Modifying the params re-serializes the whole query with the application/x-www-form-urlencoded serializer
        var url = new Url("https://example.com/?a=%20b&c=d~");
        url.SearchParams.Sort();

        Assert.Equal("?a=+b&c=d%7E", url.Search);
    }
}
