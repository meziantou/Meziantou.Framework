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
}
