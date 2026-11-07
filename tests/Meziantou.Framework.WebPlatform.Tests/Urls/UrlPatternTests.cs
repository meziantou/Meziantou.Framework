using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

#pragma warning disable WEBPLATEXP0015

namespace Meziantou.Framework.WebPlatform.Tests.Urls;

public sealed class UrlPatternTests
{
    [Theory]
    [InlineData("pathname", "/foo/a", "/foo/b", -1)]
    [InlineData("pathname", "/foo/b", "/foo/bar", -1)]
    [InlineData("pathname", "/foo/bar", "/foo/:bar", 1)]
    [InlineData("pathname", "/foo/", "/foo/:bar", 1)]
    [InlineData("pathname", "/foo/:bar", "/foo/*", 1)]
    [InlineData("pathname", "/foo/{bar}", "/foo/(bar)", 1)]
    [InlineData("pathname", "/foo/{bar}", "/foo/{bar}+", 1)]
    [InlineData("pathname", "/foo/{bar}+", "/foo/{bar}?", 1)]
    [InlineData("pathname", "/foo/{bar}?", "/foo/{bar}*", 1)]
    [InlineData("pathname", "/foo/(123)", "/foo/(12)", 1)]
    [InlineData("pathname", "/foo/:b", "/foo/:a", 0)]
    [InlineData("pathname", "*/foo", "*", 1)]
    [InlineData("port", "9", "100", 1)]
    [InlineData("pathname", "foo/:bar?/baz", "foo/{:bar}?/baz", -1)]
    [InlineData("pathname", "foo/:bar?/baz", "foo{/:bar}?/baz", 0)]
    [InlineData("pathname", "foo/:bar?/baz", "fo{o/:bar}?/baz", 1)]
    [InlineData("pathname", "foo/:bar?/baz", "foo{/:bar/}?baz", -1)]
    [InlineData("protocol", "a", "b", -1)]
    [InlineData("username", "a", "b", -1)]
    [InlineData("password", "a", "b", -1)]
    [InlineData("hostname", "a", "b", -1)]
    [InlineData("search", "a", "b", -1)]
    [InlineData("hash", "a", "b", -1)]
    public void CompareComponent(string component, string leftValue, string rightValue, int expected)
    {
        var left = CreatePattern(component, leftValue);
        var right = CreatePattern(component, rightValue);

        Assert.Equal(expected, UrlPattern.CompareComponent(component, left, right));
        Assert.Equal(-expected, UrlPattern.CompareComponent(component, right, left));
        Assert.Equal(0, UrlPattern.CompareComponent(component, left, left));
        Assert.Equal(0, UrlPattern.CompareComponent(component, right, right));
    }

    [Fact]
    public void CompareComponent_UrlInput()
    {
        var left = new UrlPattern("https://a.example.com/b?a");
        var right = new UrlPattern("https://b.example.com/a?b");

        Assert.Equal(1, UrlPattern.CompareComponent("pathname", left, right));
    }

    [Fact]
    public void CompareComponent_InvalidComponent_Throws()
    {
        var pattern = new UrlPattern("/foo", "https://example.com");

        Assert.Throws<TypeErrorException>(() => UrlPattern.CompareComponent("invalid", pattern, pattern));
    }

    [Fact]
    public void Generate()
    {
        Assert.Equal("/foo", new UrlPattern(new UrlPatternInit { Pathname = "/foo" }).Generate("pathname", []));
        Assert.Equal("/bar", new UrlPattern(new UrlPatternInit { Pathname = "/:foo" }).Generate("pathname", new Dictionary<string, string> { ["foo"] = "bar" }));
        Assert.Equal("/%F0%9F%8D%85", new UrlPattern(new UrlPatternInit { Pathname = "/:foo" }).Generate("pathname", new Dictionary<string, string> { ["foo"] = "🍅" }));
        Assert.Equal("xn--fi8h.example.com", new UrlPattern(new UrlPatternInit { Hostname = "{:foo}.example.com" }).Generate("hostname", new Dictionary<string, string> { ["foo"] = "🍅" }));
        Assert.Equal("/%20", new UrlPattern("https://example.com/:foo").Generate("pathname", new Dictionary<string, string> { ["foo"] = " " }));
        Assert.Equal("/ ", new UrlPattern("original-scheme://example.com/:foo").Generate("pathname", new Dictionary<string, string> { ["foo"] = " " }));
    }

    [Fact]
    public void Generate_InvalidInput_Throws()
    {
        Assert.Throws<TypeErrorException>(() => new UrlPattern(new UrlPatternInit { Pathname = "/foo" }).Generate("invalid", []));
        Assert.Throws<TypeErrorException>(() => new UrlPattern(new UrlPatternInit { Pathname = "/:foo" }).Generate("pathname", []));
        Assert.Throws<TypeErrorException>(() => new UrlPattern(new UrlPatternInit { Pathname = "/:foo" }).Generate("pathname", new Dictionary<string, string> { ["foo"] = "bar/baz" }));
        Assert.Throws<TypeErrorException>(() => new UrlPattern(new UrlPatternInit { Pathname = "*" }).Generate("pathname", []));
        Assert.Throws<TypeErrorException>(() => new UrlPattern(new UrlPatternInit { Pathname = "/{foo}+" }).Generate("pathname", []));
        Assert.Throws<TypeErrorException>(() => new UrlPattern(new UrlPatternInit { Pathname = "/(regexp)" }).Generate("pathname", []));
        Assert.Throws<TypeErrorException>(() => new UrlPattern(new UrlPatternInit { Port = "([^\\:]+?)" }).Generate("port", []));
    }

    private static UrlPattern CreatePattern(string component, string value)
    {
        var init = component switch
        {
            "protocol" => new UrlPatternInit { Protocol = value },
            "username" => new UrlPatternInit { Username = value },
            "password" => new UrlPatternInit { Password = value },
            "hostname" => new UrlPatternInit { Hostname = value },
            "port" => new UrlPatternInit { Port = value },
            "pathname" => new UrlPatternInit { Pathname = value },
            "search" => new UrlPatternInit { Search = value },
            "hash" => new UrlPatternInit { Hash = value },
            _ => throw new ArgumentOutOfRangeException(nameof(component)),
        };

        return new UrlPattern(init);
    }

    [Fact]
    public void Exec_NamedGroups()
    {
        var pattern = new UrlPattern("https://example.com/books/:id(\\d+)/:section?");
        var result = pattern.Exec("https://example.com/books/123");

        Assert.NotNull(result);
        Assert.Equal("123", result.Pathname.Groups["id"]);
        Assert.Null(result.Pathname.Groups["section"]);
        Assert.Equal(["https://example.com/books/123"], result.Inputs.Select(i => i.StringValue));
        Assert.True(pattern.HasRegExpGroups);
        Assert.Null(pattern.Exec("https://example.com/books/abc"));
    }

    [Theory]
    [InlineData("*\u0002\0\0/..")]
    [InlineData("a/../..")]
    public void Constructor_RelativePathnameRemovingThePrefix(string pathname)
    {
        // Found by fuzzing: the canonicalization of a pathname without leading slash adds a "/-" prefix, which ".." removes
        _ = new UrlPattern(pathname, "https://example.com/a/b");
        Assert.NotNull(new UrlPattern(new UrlPatternInit { Pathname = pathname }).Pathname);
    }

    [Fact]
    public void Constructor_Init()
    {
        var pattern = new UrlPattern(new UrlPatternInit { Hostname = "{*.}?example.com", Pathname = "/api/*" });

        Assert.Equal("*", pattern.Protocol);
        Assert.Equal("{*.}?example.com", pattern.Hostname);
        Assert.True(pattern.Test("https://api.example.com/api/users"));
        Assert.True(pattern.Test(new UrlPatternInit { Hostname = "example.com", Pathname = "/api/" }));
        Assert.False(pattern.Test("https://example.org/api/users"));
    }

    [Fact]
    public void Constructor_RelativeWithoutBaseUrl_Throws()
    {
        Assert.Throws<TypeErrorException>(() => new UrlPattern("/books/:id"));
        Assert.Throws<TypeErrorException>(() => new UrlPattern(new UrlPatternInit(), "https://example.com"));
        Assert.Equal("/books/:id", new UrlPattern("/books/:id", "https://example.com").Pathname);
    }

    [Fact]
    public void Constructor_InvalidRegExp_Throws()
    {
        Assert.Throws<TypeErrorException>(() => new UrlPattern(new UrlPatternInit { Pathname = "/(\\m)" }));
        Assert.Throws<TypeErrorException>(() => new UrlPattern(new UrlPatternInit { Pathname = "/:a/:a" }));
    }

    [Fact]
    public void IgnoreCase()
    {
        var pattern = new UrlPattern(new UrlPatternInit { Pathname = "/Foo/:bar" }, new UrlPatternOptions { IgnoreCase = true });

        Assert.Equal("Baz", pattern.Exec(new UrlPatternInit { Pathname = "/fOO/Baz" })?.Pathname.Groups["bar"]);
        Assert.False(new UrlPattern(new UrlPatternInit { Pathname = "/Foo" }).Test(new UrlPatternInit { Pathname = "/foo" }));
    }

    [Fact]
    public void Exec_Url()
    {
        var pattern = new UrlPattern("https://example.com/:path*");
        var url = new Url("https://example.com/a/b?q=1");

        var result = pattern.Exec(url);

        Assert.NotNull(result);
        Assert.Equal("https://example.com/a/b?q=1", result.Inputs[0].StringValue);
        Assert.Equal("a/b", result.Pathname.Groups["path"]);
    }

    [Theory]
    [InlineData("^(.*)(.*)?$", "foobar", new[] { "foobar", null })] // An empty iteration of a quantifier fails once the minimum is reached
    [InlineData("^(a*)?b$", "b", new string?[] { null })]
    [InlineData("^((a*?))?b$", "ab", new[] { "a", "a" })]
    [InlineData("^(?<x>a)(b)$", "ab", new[] { "a", "b" })] // Named groups are numbered in source order
    [InlineData("^([[a-z]--[aeiou]]+)$", "bcd", new[] { "bcd" })]
    [InlineData("^([\\d&&[0-4]])$", "3", new[] { "3" })]
    [InlineData("^([\\q{abc|d}])$", "abc", new[] { "abc" })]
    [InlineData("^(.)$", "\U0001F345", new[] { "\U0001F345" })] // "." matches a code point
    [InlineData("^([^a])$", "\U0001F345", new[] { "\U0001F345" })]
    [InlineData("^([\\u{1F300}-\\u{1F3FF}])$", "\U0001F345", new[] { "\U0001F345" })]
    [InlineData("^(a)\\1$", "aa", new[] { "a" })]
    [InlineData("^(?<x>a)\\k<x>$", "aa", new[] { "a" })]
    [InlineData("^(\\w+)\\b", "abc def", new[] { "abc" })]
    [InlineData("^(?i:A)(b)$", "ab", new[] { "b" })]
    [InlineData("^(\\p{Lu})$", "A", new[] { "A" })]
    public void EcmaScriptRegex_Match(string pattern, string input, string?[] expectedGroups)
    {
        var regex = EcmaScriptRegex.Create(pattern, ignoreCase: false);

        Assert.NotNull(regex);
        var match = regex.Match(input);
        Assert.True(match.Success);
        Assert.Equal(expectedGroups, Enumerable.Range(1, expectedGroups.Length).Select(i => match.Groups[i].Success ? match.Groups[i].Value : null));
    }

    [Theory]
    [InlineData("^\\d$", "\U0001D7D9")] // \d is ASCII only
    [InlineData("^.$", "\n")]
    [InlineData("^..$", "\U0001F345")]
    [InlineData("^[^a]$", "a")]
    [InlineData("^[\\w--\\d]$", "1")]
    [InlineData("^a$", "a\n")]
    public void EcmaScriptRegex_NoMatch(string pattern, string input)
    {
        var regex = EcmaScriptRegex.Create(pattern, ignoreCase: false);

        Assert.NotNull(regex);
        Assert.DoesNotMatch(regex, input);
    }

    [Theory]
    [InlineData("\\m")] // Identity escapes are limited to the syntax characters with the u and v flags
    [InlineData("a{")]
    [InlineData("a}")]
    [InlineData("]")]
    [InlineData("(?R)")]
    [InlineData("[a-]")]
    [InlineData("[(]")]
    [InlineData("[a&&&b]")]
    [InlineData("[a-z&&b]")]
    [InlineData("\\1")]
    [InlineData("(?<x>a)(?<x>b)")]
    [InlineData("a{2,1}")]
    [InlineData("(?=a)*")]
    [InlineData("(?ii:a)")]
    [InlineData("\\p{Unknown}")]
    [InlineData("(a")]
    [InlineData("a)")]
    public void EcmaScriptRegex_Invalid(string pattern)
    {
        Assert.Null(EcmaScriptRegex.Create(pattern, ignoreCase: false));
    }
}
