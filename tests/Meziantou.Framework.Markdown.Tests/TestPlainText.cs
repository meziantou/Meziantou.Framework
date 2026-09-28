namespace Meziantou.Framework.Markdown.Tests;

public class TestPlainText
{
    [Theory]
    [InlineData(/* markdownText: */ "foo bar", /* expected: */ "foo bar\n")]
    [InlineData(/* markdownText: */ "foo\nbar", /* expected: */ "foo\nbar\n")]
    [InlineData(/* markdownText: */ "*foo\nbar*", /* expected: */ "foo\nbar\n")]
    [InlineData(/* markdownText: */ "[foo\nbar](http://example.com)", /* expected: */ "foo\nbar\n")]
    [InlineData(/* markdownText: */ "<http://foo.bar.baz>", /* expected: */ "http://foo.bar.baz\n")]
    [InlineData(/* markdownText: */ "<http://foo.bar/?a&b>", /* expected: */ "http://foo.bar/?a&b\n")]
    [InlineData(/* markdownText: */ "# foo bar", /* expected: */ "foo bar\n")]
    [InlineData(/* markdownText: */ "# foo\nbar", /* expected: */ "foo\nbar\n")]
    [InlineData(/* markdownText: */ "> foo", /* expected: */ "foo\n")]
    [InlineData(/* markdownText: */ "> foo\nbar\n> baz", /* expected: */ "foo\nbar\nbaz\n")]
    [InlineData(/* markdownText: */ "`foo`", /* expected: */ "foo\n")]
    [InlineData(/* markdownText: */ "`foo\nbar`", /* expected: */ "foo bar\n")] // new line within codespan is treated as whitespace (Example317)
    [InlineData(/* markdownText: */ "```\nfoo bar\n```", /* expected: */ "foo bar\n")]
    [InlineData(/* markdownText: */ "- foo\n- bar\n- baz", /* expected: */ "foo\nbar\nbaz\n")]
    [InlineData(/* markdownText: */ "- foo<baz", /* expected: */ "foo<baz\n")]
    [InlineData(/* markdownText: */ "- foo&lt;baz", /* expected: */ "foo<baz\n")]
    [InlineData(/* markdownText: */ "## foo `bar::baz >`", /* expected: */ "foo bar::baz >\n")]
    [InlineData(/* markdownText: */ "<div>\nx\n</div>\n\nfoo", /* expected: */ "foo\n")]
    public void TestPlainEnsureNewLine(string markdownText, string expected)
    {
        var actual = MarkdownConverter.ToPlainText(markdownText);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(/* markdownText: */ "```\nConsole.WriteLine(\"Hello, World!\");\n```", /* expected: */ "Console.WriteLine(\"Hello, World!\");\n")]
    public void TestPlainCodeBlock(string markdownText, string expected)
    {
        var actual = MarkdownConverter.ToPlainText(markdownText);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(/* markdownText: */ ":::\nfoo\n:::", /* expected: */ "foo\n", /*extensions*/ "customcontainers|advanced")]
    [InlineData(/* markdownText: */ ":::bar\nfoo\n:::", /* expected: */ "foo\n", /*extensions*/ "customcontainers+attributes|advanced")]
    [InlineData(/* markdownText: */ "```mermaid\nA --> B & C <x> \"q\"\n```", /* expected: */ "A --> B & C <x> \"q\"\n", /*extensions*/ "diagrams|advanced")]
    [InlineData(/* markdownText: */ "| Header1 | Header2 | Header3 |\n|--|--|--|\nt**es**t|value2|value3", /* expected: */ "Header1 Header2 Header3 test value2 value3","pipetables")]
    [InlineData(/* markdownText: */ "Term 1\nTerm 2\n:   Definition 1\n:   Definition 2\n\n    More", /* expected: */ "Term 1\nTerm 2\nDefinition 1\nDefinition 2\nMore", "definitionlists")]
    [InlineData(/* markdownText: */ "^^^\n![alt](a.png)\n^^^ The *caption*", /* expected: */ "alt\nThe caption", "figures")]
    [InlineData(/* markdownText: */ "a\n\n^^ The *footer*\n^^ on two lines", /* expected: */ "a\nThe footer\non two lines", "footers")]
    [InlineData(/* markdownText: */ "a[^1] b[^2]\n\n[^1]: First *note*\n[^2]: Second note", /* expected: */ "a1 b2\nFirst note\nSecond note", "footnotes")]
    public void TestPlainWithExtensions(string markdownText, string expected, string extensions)
    {
        TestParser.TestSpec(markdownText, expected, extensions, plainText: true);
    }

    internal static void TestSpec(string markdownText, string expected, string extensions, string? context = null)
    {
        TestParser.TestSpec(markdownText, expected, extensions, plainText: true, context: context);
    }
}
