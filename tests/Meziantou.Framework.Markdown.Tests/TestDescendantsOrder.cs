using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public static class TestDescendantsOrder
{
    public static void TestSchemas(MarkdownDocument[] specsSyntaxTrees)
    {
        foreach (var syntaxTree in specsSyntaxTrees)
        {
            AssertIEnumerablesAreEqual(
                Descendants_Legacy(syntaxTree),
                syntaxTree.Descendants());

            AssertIEnumerablesAreEqual(
                syntaxTree.Descendants().OfType<ParagraphBlock>(),
                syntaxTree.Descendants<ParagraphBlock>());

            AssertIEnumerablesAreEqual(
                syntaxTree.Descendants().OfType<ParagraphBlock>(),
                (syntaxTree as ContainerBlock).Descendants<ParagraphBlock>());

            AssertIEnumerablesAreEqual(
                syntaxTree.Descendants().OfType<LiteralInline>(),
                syntaxTree.Descendants<LiteralInline>());

            foreach (LiteralInline literalInline in syntaxTree.Descendants<LiteralInline>())
            {
                Assert.Same(Array.Empty<ListBlock>(), literalInline.Descendants<ListBlock>());
                Assert.Same(Array.Empty<ParagraphBlock>(), literalInline.Descendants<ParagraphBlock>());
                Assert.Same(Array.Empty<ContainerInline>(), literalInline.Descendants<ContainerInline>());
            }

            foreach (ContainerInline containerInline in syntaxTree.Descendants<ContainerInline>())
            {
                AssertIEnumerablesAreEqual(
                    containerInline.FindDescendants<LiteralInline>(),
                    containerInline.Descendants<LiteralInline>());

                AssertIEnumerablesAreEqual(
                    containerInline.FindDescendants<LiteralInline>(),
                    (containerInline as MarkdownObject).Descendants<LiteralInline>());

                if (containerInline.FirstChild is null)
                {
                    Assert.Same(Array.Empty<LiteralInline>(), containerInline.Descendants<LiteralInline>());
                    Assert.Same(Array.Empty<LiteralInline>(), containerInline.FindDescendants<LiteralInline>());
                    Assert.Same(Array.Empty<LiteralInline>(), (containerInline as MarkdownObject).Descendants<LiteralInline>());
                }

                Assert.Same(Array.Empty<ListBlock>(), containerInline.Descendants<ListBlock>());
                Assert.Same(Array.Empty<ParagraphBlock>(), containerInline.Descendants<ParagraphBlock>());
            }

            foreach (ParagraphBlock paragraphBlock in syntaxTree.Descendants<ParagraphBlock>())
            {
                AssertIEnumerablesAreEqual(
                    (paragraphBlock as MarkdownObject).Descendants<LiteralInline>(),
                    paragraphBlock.Descendants<LiteralInline>());

                Assert.Same(Array.Empty<ParagraphBlock>(), paragraphBlock.Descendants<ParagraphBlock>());

                if (paragraphBlock.Inline is not null)
                {
                    AssertIEnumerablesAreEqual(
                        Descendants_Legacy(paragraphBlock.Inline),
                        paragraphBlock.Descendants());

                    AssertIEnumerablesAreEqual(
                        paragraphBlock.Inline.FindDescendants<LiteralInline>(),
                        paragraphBlock.Descendants<LiteralInline>());
                }
            }

            foreach (ContainerBlock containerBlock in syntaxTree.Descendants<ContainerBlock>())
            {
                AssertIEnumerablesAreEqual(
                    containerBlock.Descendants<LiteralInline>(),
                    (containerBlock as MarkdownObject).Descendants<LiteralInline>());

                AssertIEnumerablesAreEqual(
                    containerBlock.Descendants<ParagraphBlock>(),
                    (containerBlock as MarkdownObject).Descendants<ParagraphBlock>());

                if (containerBlock.Count == 0)
                {
                    Assert.Same(Array.Empty<LiteralInline>(), containerBlock.Descendants<LiteralInline>());
                    Assert.Same(Array.Empty<LiteralInline>(), (containerBlock as Block).Descendants<LiteralInline>());
                    Assert.Same(Array.Empty<LiteralInline>(), (containerBlock as MarkdownObject).Descendants<LiteralInline>());
                }
            }
        }
    }

    private static void AssertIEnumerablesAreEqual<T>(IEnumerable<T> first, IEnumerable<T> second)
    {
        var firstList = new List<T>(first);
        var secondList = new List<T>(second);

        Assert.HasCount(firstList.Count, secondList);

        for (int i = 0; i < firstList.Count; i++)
        {
            Assert.Same(firstList[i], secondList[i]);
        }
    }

    private static IEnumerable<MarkdownObject> Descendants_Legacy(MarkdownObject markdownObject)
    {
        // Recursive reference implementation, used to check the order of the items returned by Descendants

        var block = markdownObject as ContainerBlock;
        if (block is not null)
        {
            foreach (var subBlock in block)
            {
                yield return subBlock;

                foreach (var sub in Descendants_Legacy(subBlock))
                {
                    yield return sub;
                }

                // Visit leaf block that have inlines
                var leafBlock = subBlock as LeafBlock;
                if (leafBlock?.Inline is not null)
                {
                    foreach (var subInline in Descendants_Legacy(leafBlock.Inline))
                    {
                        yield return subInline;
                    }
                }
            }
        }
        else
        {
            var inline = markdownObject as ContainerInline;
            if (inline is not null)
            {
                var child = inline.FirstChild;
                while (child is not null)
                {
                    var next = child.NextSibling;
                    yield return child;

                    foreach (var sub in Descendants_Legacy(child))
                    {
                        yield return sub;
                    }

                    child = next;
                }
            }
        }
    }
}
