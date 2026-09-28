using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Tests;

public class TestLazySubstring
{
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("foo")]
    public void LazySubstring_ReturnsCorrectSubstring(string text)
    {
        var substring = new LazySubstring(text);
        Assert.Equal(0, substring.Offset);
        Assert.Equal(text.Length, substring.Length);

        Assert.Equal(text, substring.AsSpan().ToString());
        Assert.Equal(text, substring.AsSpan().ToString());
        Assert.Equal(0, substring.Offset);
        Assert.Equal(text.Length, substring.Length);

        Assert.Same(substring.ToString(), substring.ToString());
        Assert.Equal(text, substring.ToString());
        Assert.Equal(0, substring.Offset);
        Assert.Equal(text.Length, substring.Length);

        Assert.Equal(text, substring.AsSpan().ToString());
        Assert.Equal(text, substring.AsSpan().ToString());
        Assert.Equal(0, substring.Offset);
        Assert.Equal(text.Length, substring.Length);
    }

    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("a", 0, 0)]
    [InlineData("a", 1, 0)]
    [InlineData("a", 0, 1)]
    [InlineData("foo", 1, 0)]
    [InlineData("foo", 1, 1)]
    [InlineData("foo", 1, 2)]
    [InlineData("foo", 0, 3)]
    public void LazySubstring_ReturnsCorrectSubstring_WithRange(string text, int start, int length)
    {
        var substring = new LazySubstring(text, start, length);
        Assert.Equal(start, substring.Offset);
        Assert.Equal(length, substring.Length);

        string expectedSubstring = text.Substring(start, length);

        Assert.Equal(expectedSubstring, substring.AsSpan().ToString());
        Assert.Equal(expectedSubstring, substring.AsSpan().ToString());
        Assert.Equal(start, substring.Offset);
        Assert.Equal(length, substring.Length);

        Assert.Same(substring.ToString(), substring.ToString());
        Assert.Equal(expectedSubstring, substring.ToString());
        Assert.Equal(0, substring.Offset);
        Assert.Equal(length, substring.Length);

        Assert.Equal(expectedSubstring, substring.AsSpan().ToString());
        Assert.Equal(expectedSubstring, substring.AsSpan().ToString());
        Assert.Equal(0, substring.Offset);
        Assert.Equal(length, substring.Length);
    }
}
