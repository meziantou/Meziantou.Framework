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
}
