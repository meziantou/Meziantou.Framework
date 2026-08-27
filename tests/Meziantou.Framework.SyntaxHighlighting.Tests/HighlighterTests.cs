namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class HighlighterTests
{
    [Fact]
    public void Highlight()
    {
        AssertHighlighter("csharp",
            """
            public class MyClass { }
            """,
            """
            <span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">MyClass</span> { }
            """);
    }

    [Fact]
    public void Highlight_CustomClassPrefix()
    {
        var result = SyntaxHighlighter.Highlight(
            "public class MyClass { }",
            "csharp",
            new HighlightOptions { ClassPrefix = "syntax-" });

        Assert.Equal(
            """<span class="syntax-keyword">public</span> <span class="syntax-keyword">class</span> <span class="syntax-title">MyClass</span> { }""",
            result);
    }

    [Fact]
    public void Highlight_DifferentOptions_DoNotCrossContaminate()
    {
        const string Code = "public class C { }";

        var defaultResult = SyntaxHighlighter.Highlight(Code, "csharp");
        var customResult = SyntaxHighlighter.Highlight(Code, "csharp", new HighlightOptions { ClassPrefix = "x-" });
        var defaultResult2 = SyntaxHighlighter.Highlight(Code, "csharp");

        Assert.Contains("hljs-keyword", defaultResult);
        Assert.DoesNotContain("x-keyword", defaultResult);
        Assert.Contains("x-keyword", customResult);
        Assert.DoesNotContain("hljs-keyword", customResult);
        Assert.Equal(defaultResult, defaultResult2);
    }

    [Fact]
    public void Highlight_NullText_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => SyntaxHighlighter.Highlight(text: null!, "csharp"));

        Assert.Equal("text", exception.ParamName);
    }

    [Fact]
    public void Highlight_NullLanguage_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => SyntaxHighlighter.Highlight("class C { }", language: null!));

        Assert.Equal("language", exception.ParamName);
    }

    [Fact]
    public void Highlight_UnknownLanguage_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => SyntaxHighlighter.Highlight("class C { }", "not-a-language"));
    }

    [Fact]
    public void Highlight_EmptyText_ReturnsEmptyString()
    {
        Assert.Empty(SyntaxHighlighter.Highlight("", "csharp"));
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("cs")]
    [InlineData("c#")]
    [InlineData("CSharp")]
    [InlineData("C#")]
    [InlineData("YAML")]
    public void IsSupported_KnownLanguage_ReturnsTrue(string language)
    {
        Assert.True(SyntaxHighlighter.IsSupported(language));
    }

    [Theory]
    [InlineData("not-a-language")]
    [InlineData("")]
    [InlineData("rust")]
    public void IsSupported_UnknownLanguage_ReturnsFalse(string language)
    {
        Assert.False(SyntaxHighlighter.IsSupported(language));
    }

    [Fact]
    public void IsSupported_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SyntaxHighlighter.IsSupported(null!));
    }

    [Fact]
    public void TryHighlight_KnownLanguage_ReturnsTrueAndMarkup()
    {
        Assert.True(SyntaxHighlighter.TryHighlight("class C { }", "csharp", out var html));
        Assert.Equal("""<span class="hljs-keyword">class</span> <span class="hljs-title">C</span> { }""", html);
    }

    [Fact]
    public void TryHighlight_UnknownLanguage_ReturnsFalse()
    {
        Assert.False(SyntaxHighlighter.TryHighlight("class C { }", "not-a-language", out var html));
        Assert.Null(html);
    }

    [Fact]
    public void TryHighlight_HonoursOptions()
    {
        Assert.True(SyntaxHighlighter.TryHighlight("class C { }", "csharp", out var html, new HighlightOptions { ClassPrefix = "x-" }));
        Assert.Contains("x-keyword", html);
    }

    [Fact]
    public void GetSupportedLanguages_HasNoDuplicates()
    {
        var duplicates = SyntaxHighlighter.GetSupportedLanguages()
            .GroupBy(language => language, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// Ties the advertised list to reality: every identifier the registry reports must actually
    /// resolve to a grammar that can highlight, so the list cannot drift from what works.
    /// </summary>
    [Fact]
    public void GetSupportedLanguages_EveryEntryCanHighlight()
    {
        foreach (var language in SyntaxHighlighter.GetSupportedLanguages())
        {
            Assert.True(SyntaxHighlighter.IsSupported(language));
            Assert.True(SyntaxHighlighter.TryHighlight("x = 1", language, out var html));
            Assert.NotNull(html);
        }
    }
}
