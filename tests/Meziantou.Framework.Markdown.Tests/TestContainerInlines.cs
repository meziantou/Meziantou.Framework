using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public class TestContainerInlines
{
    private sealed class MockLeafBlock : LeafBlock
    {
        public MockLeafBlock()
            : base(null)
        {

        }
    }

    [Fact]
    public void CanBeAddedToLeafBlock()
    {
        var leafBlock1 = new MockLeafBlock();

        var one = new ContainerInline();
        Assert.Null(one.ParentBlock);

        leafBlock1.Inline = one;
        Assert.Same(leafBlock1, one.ParentBlock);

        var two = new ContainerInline();
        Assert.Null(two.ParentBlock);

        leafBlock1.Inline = two;
        Assert.Same(leafBlock1, two.ParentBlock);
        Assert.Null(one.ParentBlock);

        var leafBlock2 = new MockLeafBlock();
        Assert.Throws<ArgumentException>(() => leafBlock2.Inline = two);
    }

    [Fact]
    public void CanTransferChildrenToAnotherContainer()
    {
        var source = new ContainerInline();
        var first = new LiteralInline("a");
        var second = new LiteralInline("b");
        source.AppendChild(first);
        source.AppendChild(second);

        var destination = new ContainerInline();
        var existing = new LiteralInline("x");
        destination.AppendChild(existing);

        source.TransferChildrenTo(destination);

        Assert.Null(source.FirstChild);
        Assert.Null(source.LastChild);
        Assert.Same(existing, destination.FirstChild);
        Assert.Same(first, existing.NextSibling);
        Assert.Same(second, first.NextSibling);
        Assert.Null(second.NextSibling);
        Assert.Same(destination, first.Parent);
        Assert.Same(destination, second.Parent);
    }

    [Fact]
    public void ReplaceByMovesChildrenToTheParentOfTheReplacement()
    {
        var root = new ContainerInline();
        var outer = new ContainerInline();
        var inner = new ContainerInline();
        var x = new LiteralInline("x");
        var a = new LiteralInline("a");
        var b = new LiteralInline("b");
        root.AppendChild(outer);
        outer.AppendChild(x);
        outer.AppendChild(inner);
        inner.AppendChild(a);
        inner.AppendChild(b);

        var innerLiteral = new LiteralInline("[");
        Assert.Same(b, inner.ReplaceBy(innerLiteral));
        Assert.Same(outer, a.Parent);
        Assert.Same(outer, b.Parent);
        Assert.Same(b, outer.LastChild);

        var outerLiteral = new LiteralInline("[");
        Assert.Same(b, outer.ReplaceBy(outerLiteral));

        Assert.Equal(new Inline[] { outerLiteral, x, innerLiteral, a, b }, root.ToArray());
        Assert.Null(outerLiteral.PreviousSibling);
        Assert.Same(a, b.PreviousSibling);
        Assert.Same(b, root.LastChild);
        Assert.All(root, child => Assert.Same(root, child.Parent));
        Assert.Null(outer.FirstChild);
        Assert.Null(outer.LastChild);
        Assert.Null(outer.Parent);
        Assert.Null(inner.FirstChild);
        Assert.Null(inner.Parent);
    }

    [Fact]
    public void ReplacedContainerCanBeReused()
    {
        var root = new ContainerInline();
        var outer = new ContainerInline();
        var inner = new ContainerInline();
        var a = new LiteralInline("a");
        var b = new LiteralInline("b");
        root.AppendChild(outer);
        outer.AppendChild(inner);
        inner.AppendChild(a);
        inner.AppendChild(b);
        inner.ReplaceBy(new LiteralInline("["));
        outer.ReplaceBy(new LiteralInline("["));

        var c = new LiteralInline("c");
        inner.AppendChild(c);
        var d = new LiteralInline("d");
        outer.AppendChild(d);
        outer.AppendChild(inner);

        Assert.Same(inner, c.Parent);
        Assert.Same(outer, d.Parent);
        Assert.Same(outer, inner.Parent);
        Assert.Same(root, a.Parent);
        Assert.Same(root, b.Parent);
        Assert.Equal(new Inline[] { c }, inner.ToArray());
        Assert.Equal(new Inline[] { d, inner }, outer.ToArray());

        b.Remove();
        Assert.Same(a, root.LastChild);
        Assert.Null(b.Parent);
    }

    [Fact]
    public void ReplaceByOpenContainerMovesChildrenIntoIt()
    {
        var root = new ContainerInline();
        var delimiter = new ContainerInline();
        var a = new LiteralInline("a");
        var b = new LiteralInline("b");
        root.AppendChild(new LiteralInline("x"));
        root.AppendChild(delimiter);
        delimiter.AppendChild(a);
        delimiter.AppendChild(b);

        var replacement = new ContainerInline();
        var existing = new LiteralInline("e");
        replacement.AppendChild(existing);

        Assert.Same(b, delimiter.ReplaceBy(replacement));

        Assert.Same(replacement, root.LastChild);
        Assert.Same(root, replacement.Parent);
        Assert.Equal(new Inline[] { existing, a, b }, replacement.ToArray());
        Assert.Same(replacement, a.Parent);
        Assert.Same(replacement, b.Parent);
        Assert.Same(existing, a.PreviousSibling);
        Assert.Same(b, replacement.LastChild);
    }

    [Fact]
    public void ClearDetachesTheChildren()
    {
        var container = new ContainerInline();
        var a = new LiteralInline("a");
        var b = new LiteralInline("b");
        var c = new LiteralInline("c");
        container.AppendChild(a);
        container.AppendChild(b);
        container.AppendChild(c);

        container.Clear();

        Assert.Null(container.FirstChild);
        Assert.Null(container.LastChild);
        Assert.Empty(container);
        foreach (var child in new Inline[] { a, b, c })
        {
            Assert.Null(child.Parent);
            Assert.Null(child.PreviousSibling);
            Assert.Null(child.NextSibling);
        }

        // The former siblings must not link the children appended again to the ones that were not
        container.AppendChild(a);
        container.AppendChild(c);
        Assert.Equal(new Inline[] { a, c }, container.Take(5).ToArray());
        Assert.Same(a, c.PreviousSibling);
        Assert.Null(c.NextSibling);

        var other = new ContainerInline();
        other.AppendChild(b);
        Assert.Equal(new Inline[] { b }, other.ToArray());
    }

    [Fact]
    public void ClearDetachesTheChildrenMovedFromAReplacedContainer()
    {
        var root = new ContainerInline();
        var delimiter = new ContainerInline();
        var a = new LiteralInline("a");
        var b = new LiteralInline("b");
        root.AppendChild(delimiter);
        delimiter.AppendChild(a);
        delimiter.AppendChild(b);
        delimiter.ReplaceBy(new LiteralInline("["));
        Assert.Same(root, a.Parent);

        root.Clear();

        Assert.Empty(root);
        Assert.Null(a.Parent);
        Assert.Null(b.Parent);
        Assert.Null(a.NextSibling);
        Assert.Null(b.PreviousSibling);

        var other = new ContainerInline();
        other.AppendChild(b);
        other.AppendChild(a);
        Assert.Equal(new Inline[] { b, a }, other.Take(5).ToArray());
        Assert.Same(other, a.Parent);
    }

    [Fact]
    public void InsertBeforeLinksBothSiblings()
    {
        var container = new ContainerInline();
        var a = new LiteralInline("a");
        var c = new LiteralInline("c");
        container.AppendChild(a);
        container.AppendChild(c);

        var b = new LiteralInline("b");
        c.InsertBefore(b);
        var first = new LiteralInline("0");
        a.InsertBefore(first);

        Assert.Equal(new Inline[] { first, a, b, c }, container.ToArray());
        Assert.Same(first, container.FirstChild);
        Assert.Null(first.PreviousSibling);
        Assert.Same(a, b.PreviousSibling);
        Assert.Same(c, b.NextSibling);
        Assert.Same(b, c.PreviousSibling);
        Assert.Same(container, b.Parent);

        var backward = new List<Inline>();
        for (var inline = container.LastChild; inline is not null && backward.Count < 5; inline = inline.PreviousSibling)
        {
            backward.Add(inline);
        }

        Assert.Equal(new Inline[] { c, b, a, first }, backward);
    }

    [Fact]
    public void ContainsParentOrSiblingOfType()
    {
        var root = new ContainerInline();
        var html = new HtmlInline("<b>");
        var link = new LinkDelimiterInline(new LinkInlineParser());
        var emphasis = new EmphasisInline();
        var text = new LiteralInline("x");
        root.AppendChild(html);
        root.AppendChild(link);
        link.AppendChild(emphasis);
        emphasis.AppendChild(text);

        Assert.True(text.ContainsParentOrSiblingOfType<LinkDelimiterInline>());
        Assert.True(text.ContainsParentOrSiblingOfType<EmphasisInline>());
        Assert.True(text.ContainsParentOrSiblingOfType<LiteralInline>());
        Assert.True(text.ContainsParentOrSiblingOfType<HtmlInline>());
        Assert.True(html.ContainsParentOrSiblingOfType<LinkDelimiterInline>());
        Assert.False(text.ContainsParentOrSiblingOfType<CodeInline>());
        Assert.False(html.ContainsParentOrSiblingOfType<EmphasisInline>());
        Assert.False(root.ContainsParentOrSiblingOfType<HtmlInline>());
        Assert.False(new LiteralInline("y").ContainsParentOrSiblingOfType<HtmlInline>());
    }

    [Fact]
    public void FindBestParent()
    {
        var root = new ContainerInline();
        var outer = new ContainerInline();
        var text = new LiteralInline("x");
        root.AppendChild(new LiteralInline("a"));
        root.AppendChild(outer);
        outer.AppendChild(text);

        Assert.Same(root, text.FindBestParent());
        Assert.Same(root, outer.FindBestParent());
        Assert.Same(root, root.FindBestParent());

        // Without a parent, the first of the siblings
        var first = new LiteralInline("1");
        var second = new LiteralInline("2");
        var third = new LiteralInline("3");
        first.InsertAfter(second);
        second.InsertAfter(third);
        Assert.Same(first, third.FindBestParent());
    }

    [Theory]
    [InlineData("*a *b %c c* d*", "<p><em>a <em>{c} c</em> d</em></p>\n")]
    [InlineData("*a %c b*", "<p><em>{c} b</em></p>\n")]
    [InlineData("[a [b %d](/u) c](/v)", "<p><a href=\"/v\">a [b {d}](/u) c</a></p>\n")]
    [InlineData("[a [b %d] c](/v)", "<p><a href=\"/v\">a [b {d}] c</a></p>\n")]
    [InlineData("[a ![b %i](/u) c](/v)", "<p>[a <a href=\"/u\">b {i}</a> c](/v)</p>\n")]
    [InlineData("[a [b %i](/u) c](/v)", "<p><a href=\"/v\">a <img src=\"/u\" alt=\"b {i}\" /> c</a></p>\n")]
    [InlineData("*a <a href=\"x\"> %h www.x.com*", "<p><em>a <b> {h} <a href=\"http://www.x.com\">www.x.com</a></em></p>\n")]
    [InlineData("*a <b> %a www.x.com*", "<p><em>a <a href=\"y\"> {a} www.x.com</em></p>\n")]
    public void ChangesToOpenContainersDuringParsingAreTracked(string markdown, string expected)
    {
        // A tracked chain of open containers must give the same result as walking the containers. The unmatched delimiter
        // before the text puts the containers deep enough in the chain to be tracked.
        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, CreatePipeline(minimumThresholds: false)));
        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, CreatePipeline(minimumThresholds: true)));
        Assert.Equal("<p>_x " + expected[3..], MarkdownConverter.ToHtml("_x " + markdown, CreatePipeline(minimumThresholds: true)));

        static MarkdownPipeline CreatePipeline(bool minimumThresholds)
        {
            var builder = new MarkdownPipelineBuilder().UseAutoLinks();
            builder.InlineParsers.Insert(0, new OpenContainerMutatingParser());
            var pipeline = builder.Build();
            return minimumThresholds ? TestParser.UseMinimumThresholds(pipeline) : pipeline;
        }
    }

    // Changes the containers that are still open when "%" is followed by: 'c' clears the deepest one, 'd' deactivates the
    // nearest link delimiter, 'i' toggles whether it is an image, 'h' and 'a' change the tag of the last raw HTML inline
    private sealed class OpenContainerMutatingParser : InlineParser
    {
        public OpenContainerMutatingParser()
        {
            OpeningCharacters = ['%'];
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            var action = slice.PeekChar();
            var container = processor.Root!;
            while (container.LastChild is ContainerInline { IsClosed: false } child)
            {
                container = child;
            }

            switch (action)
            {
                case 'c':
                    container.Clear();
                    break;

                case 'd':
                    container.FirstParentOfType<LinkDelimiterInline>()!.IsActive = false;
                    break;

                case 'i':
                    var linkDelimiter = container.FirstParentOfType<LinkDelimiterInline>()!;
                    linkDelimiter.IsImage = !linkDelimiter.IsImage;
                    break;

                case 'h':
                case 'a':
                    container.FindDescendants<HtmlInline>().Last().Tag = action == 'h' ? "<b>" : "<a href=\"y\">";
                    break;

                default:
                    return false;
            }

            slice.Start += 2;
            processor.Inline = new LiteralInline("{" + action + "}");
            return true;
        }
    }
}
