using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>https://html.spec.whatwg.org/multipage/input.html#the-pattern-attribute</summary>
public sealed class InputPatternTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private bool IsPatternMismatch(string pattern, string value)
    {
        var context = _contexts.Create(new BrowsingContextOptions());
        var document = context.Navigate(_ => "<input>");
        var input = (HtmlInputElement)document.QuerySelector("input")!;
        input.SetAttribute("pattern", pattern);
        input.Value = value;
        return input.Validity.PatternMismatch;
    }

    [Theory]
    [InlineData("[a-z]+", "abc", false)]
    [InlineData("[a-z]+", "abc1", true)]
    [InlineData("a|b", "ab", true)] // The pattern is anchored as a whole: ^(?:a|b)$
    [InlineData(@"[\p{L}--[a-z]]+", "ÉA", false)] // Class set subtraction
    [InlineData(@"[\p{L}--[a-z]]+", "Éa", true)]
    [InlineData(@"[\w&&\d]+", "123", false)] // Class set intersection
    [InlineData(@"[\w&&\d]+", "12a", true)]
    [InlineData(@"[\q{abc|d}]+", "abcd", false)] // String literals in a class
    [InlineData(@"[\q{abc|d}]+", "ab", true)]
    [InlineData(@"[[a-c][x-z]]+", "axbz", false)] // Nested classes
    [InlineData(@"[[a-c][x-z]]+", "ad", true)]
    [InlineData(@"[\p{ASCII_Hex_Digit}--[Ff]]+", "0123456789abcdeABCDE", false)]
    [InlineData(@"[\p{ASCII_Hex_Digit}--[Ff]]+", "0f", true)]
    [InlineData(@"\p{AHex}+", "ｆ", true)]
    [InlineData(@"\p{Hex_Digit}+", "ｆ", false)]
    [InlineData(@"\d+", "٣", true)] // \d only matches ASCII digits
    [InlineData(@"\w+", "é", true)] // \w only matches ASCII word characters
    public void PatternUsesTheUnicodeSetsMode(string pattern, string value, bool mismatch)
    {
        Assert.Equal(mismatch, IsPatternMismatch(pattern, value));
    }

    [Fact]
    public void PatternMatchesCodePoints()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);
        Assert.False(IsPatternMismatch(".", emoji));
        Assert.True(IsPatternMismatch("..", emoji));
        Assert.False(IsPatternMismatch(@"[^a]", emoji));
    }

    [Theory]
    [InlineData(@"\m")] // Identity escapes are not allowed with the v flag
    [InlineData("[(]")] // ( ) [ ] { } / - | must be escaped in a class
    [InlineData("[a-]")]
    [InlineData("[a&&&b]")]
    [InlineData("a)(b")] // Invalid on its own, even though ^(?:a)(b)$ is valid
    [InlineData("(")]
    [InlineData("a{2,1}")]
    [InlineData(@"\p{Unknown}")]
    public void InvalidPatternIsIgnored(string pattern)
    {
        Assert.False(IsPatternMismatch(pattern, "anything"));
    }
}
