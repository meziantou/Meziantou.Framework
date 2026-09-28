using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Tests;

public class TestHtmlCodeBlocks
{
    // Start condition: line begins with the string < or </ followed by one of the strings (case-insensitive)
    // {list of all tags}, followed by a space, a tab, the end of the line, the string >, or the string />.
    public static TheoryData<string> KnownSimpleHtmlTags => new(
    [
        "address", "article", "aside", "base", "basefont", "blockquote", "body", "caption", "center", "col", "colgroup", "dd", "details",
        "dialog", "dir", "div", "dl", "dt", "fieldset", "figcaption", "figure", "footer", "form", "frame", "frameset",
        "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hr", "html", "iframe", "legend", "li", "link",
        "main", "menu", "menuitem", "nav", "noframes", "ol", "optgroup", "option", "p", "param",
        "search", "section", "summary", "table", "tbody", "td", "tfoot", "th", "thead", "title", "tr", "track", "ul",
    ]);

    [Theory]
    [MemberData(nameof(KnownSimpleHtmlTags))]
    public void TestKnownTags(string tag)
    {
        MarkdownDocument document = MarkdownConverter.Parse(
            $"""
            Hello
             <{tag} />
            World
            """.ReplaceLineEndings("\n"));

        HtmlBlock[] htmlBlocks = document.Descendants<HtmlBlock>().ToArray();

        Assert.HasCount(1, htmlBlocks);
        Assert.Equal(7, htmlBlocks[0].Span.Start);
        Assert.Equal(10 + tag.Length, htmlBlocks[0].Span.Length);
    }
}