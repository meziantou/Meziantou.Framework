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
}
