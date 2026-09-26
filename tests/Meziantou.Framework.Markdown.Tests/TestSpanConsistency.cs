using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public sealed class TestSpanConsistency
{
    private sealed class MockContainerBlock : ContainerBlock
    {
        public MockContainerBlock() : base(null)
        {
        }
    }

    [Fact]
    public void BlockUpdateSpanToIncludePropagatesToParents()
    {
        var root = new MockContainerBlock { Span = new SourceSpan(30, 40) };
        var child = new MockContainerBlock { Span = new SourceSpan(32, 35) };
        root.Add(child);

        child.UpdateSpanToInclude(new SourceSpan(10, 50));

        Assert.Equal(new SourceSpan(10, 50), child.Span);
        Assert.Equal(new SourceSpan(10, 50), root.Span);
    }

    [Fact]
    public void ContainerBlockCanValidateAndUpdateSpansRecursively()
    {
        var document = new MarkdownDocument();
        var container = new MockContainerBlock { Span = new SourceSpan(30, 31) };
        var paragraph = new ParagraphBlock { Span = new SourceSpan(23, 24) };
        paragraph.Inline = new ContainerInline { Span = new SourceSpan(22, 24) };

        container.Add(paragraph);
        document.Add(container);
        document.Span = new SourceSpan(0, 5);

        Assert.False(document.HasValidSpan(recursive: true));

        Assert.True(document.UpdateSpanFromChildren(recursive: true));
        Assert.True(document.HasValidSpan(recursive: true));
        Assert.Equal(new SourceSpan(22, 24), paragraph.Span);
        Assert.Equal(new SourceSpan(22, 31), container.Span);
        Assert.Equal(new SourceSpan(0, 31), document.Span);

        container.Span = new SourceSpan(0, 100);
        document.Span = new SourceSpan(0, 100);

        Assert.True(document.UpdateSpanFromChildren(recursive: true, preserveSelfSpan: false));
        Assert.Equal(new SourceSpan(22, 24), container.Span);
        Assert.Equal(new SourceSpan(22, 24), document.Span);
    }

    [Fact]
    public void ContainerInlineCanValidateAndUpdateSpansRecursively()
    {
        var root = new ContainerInline { Span = new SourceSpan(35, 36) };
        var nested = new EmphasisInline { Span = new SourceSpan(50, 51) };
        var literal = new LiteralInline("x") { Span = new SourceSpan(10, 12) };

        nested.AppendChild(literal);
        root.AppendChild(nested);

        Assert.False(root.HasValidSpan(recursive: true));

        Assert.True(root.UpdateSpanFromChildren(recursive: true));
        Assert.True(root.HasValidSpan(recursive: true));
        Assert.Equal(new SourceSpan(10, 51), nested.Span);
        Assert.Equal(new SourceSpan(10, 51), root.Span);

        nested.Span = new SourceSpan(0, 100);
        root.Span = new SourceSpan(0, 100);

        Assert.True(root.UpdateSpanFromChildren(recursive: true, preserveSelfSpan: false));
        Assert.Equal(new SourceSpan(10, 12), nested.Span);
        Assert.Equal(new SourceSpan(10, 12), root.Span);
    }

    [Fact]
    public void ContainerBlockSpanCanBeExpandedAfterInsertions()
    {
        var container = new MockContainerBlock { Span = new SourceSpan(20, 25) };
        container.Insert(0, new ParagraphBlock { Span = new SourceSpan(10, 12) });
        container.Insert(1, new ParagraphBlock { Span = new SourceSpan(30, 35) });

        Assert.False(container.HasValidSpan());
        Assert.True(container.UpdateSpanFromChildren());
        Assert.Equal(new SourceSpan(10, 35), container.Span);
    }
}
