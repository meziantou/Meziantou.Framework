namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class PlaintextHighlighterTests
{
    [Fact]
    public void PlainText()
    {
        AssertHighlighter("plaintext",
"""
Hello, world!
This is just some text.
""",
"""
Hello, world!
This is just some text.
""");
    }

    [Fact]
    public void CodeIsNotHighlighted()
    {
        AssertHighlighter("plaintext",
"""
public class Program
{
    // comment
    static int x = 42; /* block */ "string" 'c' #tag @name
}
""",
"""
public class Program
{
    // comment
    static int x = 42; /* block */ &quot;string&quot; &#x27;c&#x27; #tag @name
}
""");
    }

    [Fact]
    public void HtmlIsEncoded()
    {
        AssertHighlighter("plaintext",
"""
<a href="https://example.com/?a=1&b=2">It's &amp; "quoted"</a>
""",
"""
&lt;a href=&quot;https://example.com/?a=1&amp;b=2&quot;&gt;It&#x27;s &amp;amp; &quot;quoted&quot;&lt;/a&gt;
""");
    }

    [Fact]
    public void WhitespaceIsPreserved()
    {
        AssertHighlighter("plaintext",
"""
  indented
	tab


trailing lines
""",
"""
  indented
	tab


trailing lines
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("plaintext",
"""
naïve café — 日本語 😀
""",
"""
naïve café — 日本語 😀
""");
    }

    [Theory]
    [InlineData("text")]
    [InlineData("txt")]
    public void Aliases(string language)
    {
        AssertHighlighter(language, "if (x) { return \"<b>\"; }", "if (x) { return &quot;&lt;b&gt;&quot;; }");
    }

    [Fact]
    public void LineEndingsArePreserved()
    {
        AssertHighlighter("plaintext", "a\r\nb\nc\r\n", "a\r\nb\nc\r\n");
    }
}
