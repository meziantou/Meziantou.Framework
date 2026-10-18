using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Tests.Urls;

public sealed class UrlPatternTests
{
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
