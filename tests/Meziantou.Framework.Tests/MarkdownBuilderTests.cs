using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;
public sealed class MarkdownBuilderTests
{
    private static string EscapeAndConvertToHtml(string text)
    {
        var escaped = MarkdownBuilder.Escape(text);
        var html = Markdig.Markdown.ToHtml(escaped);
        return html;
    }

    [Theory]
    [InlineData("*test*", "<p>*test*</p>\n")]
    [InlineData("**test**", "<p>**test**</p>\n")]
    [InlineData("***test***", "<p>***test***</p>\n")]
    [InlineData("_test_", "<p>_test_</p>\n")]
    [InlineData("~test~", "<p>~test~</p>\n")]
    [InlineData("1. test", "<p>1. test</p>\n")]
    [InlineData("- test", "<p>- test</p>\n")]
    [InlineData("* test", "<p>* test</p>\n")]
    [InlineData("> test", "<p>&gt; test</p>\n")]
    [InlineData("[test](url)", "<p>[test](url)</p>\n")]
    [InlineData("![test](url)", "<p>![test](url)</p>\n")]
    [InlineData("&quot;", "<p>&amp;quot;</p>\n")]
    [InlineData("# header", "<p># header</p>\n")]
    [InlineData("## header", "<p>## header</p>\n")]
    [InlineData("---", "<p>---</p>\n")]
    [InlineData("`code`", "<p>`code`</p>\n")]
    public void Escape(string text, string expected)
    {
        var html = EscapeAndConvertToHtml(text);
        html.Should().Be(expected);
    }
    [Theory]
    [InlineData("test)test", "<p><a href=\"test)test\">test</a></p>\n")]
    public void EscapeInLinkUrl(string url, string expected)
    {
        var markdown = $"[test]({MarkdownBuilder.Escape(url)})";
        var html = Markdig.Markdown.ToHtml(markdown);
        html.Should().Be(expected);
    }
}
