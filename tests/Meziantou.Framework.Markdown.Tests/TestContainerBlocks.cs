using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Tests;

public class TestContainerBlocks
{
    private sealed class MockContainerBlock : ContainerBlock
    {
        public MockContainerBlock()
            : base(null)
        {

        }
    }

    [Fact]
    public void CanBeCleared()
    {
        ContainerBlock container = new MockContainerBlock();
        Assert.Empty(container);
        Assert.Null(container.LastChild);

        var paragraph = new ParagraphBlock();
        Assert.Null(paragraph.Parent);

        container.Add(paragraph);

        Assert.HasCount(1, container);
        Assert.Same(container, paragraph.Parent);
        Assert.Same(paragraph, container.LastChild);

        container.Clear();

        Assert.Empty(container);
        Assert.Null(container.LastChild);
        Assert.Null(paragraph.Parent);
    }

    [Fact]
    public void CanBeInsertedInto()
    {
        ContainerBlock container = new MockContainerBlock();

        var one = new ParagraphBlock();
        container.Insert(0, one);
        Assert.HasCount(1, container);
        Assert.Same(container[0], one);
        Assert.Same(container, one.Parent);

        var two = new ParagraphBlock();
        container.Insert(1, two);
        Assert.HasCount(2, container);
        Assert.Same(container[0], one);
        Assert.Same(container[1], two);
        Assert.Same(container, two.Parent);

        var three = new ParagraphBlock();
        container.Insert(0, three);
        Assert.HasCount(3, container);
        Assert.Same(container[0], three);
        Assert.Same(container[1], one);
        Assert.Same(container[2], two);
        Assert.Same(container, three.Parent);

        Assert.Throws<ArgumentNullException>(() => container.Insert(0, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => container.Insert(4, new ParagraphBlock()));
        Assert.Throws<ArgumentOutOfRangeException>(() => container.Insert(-1, new ParagraphBlock()));
        Assert.Throws<ArgumentException>(() => container.Insert(0, one)); // one already has a parent
    }

    [Fact]
    public void CanBeSet()
    {
        ContainerBlock container = new MockContainerBlock();

        var one = new ParagraphBlock();
        container.Insert(0, one);
        Assert.HasCount(1, container);
        Assert.Same(container[0], one);
        Assert.Same(container, one.Parent);

        var two = new ParagraphBlock();
        container[0] = two;
        Assert.Same(container, two.Parent);
        Assert.Null(one.Parent);

        Assert.Throws<ArgumentException>(() => container[0] = two); // two already has a parent
    }

    [Fact]
    public void Contains()
    {
        var container = new MockContainerBlock();
        var block = new ParagraphBlock();

        Assert.DoesNotContain(block, container);

        container.Add(block);
        Assert.Contains(block, container);

        container.Add(new ParagraphBlock());
        Assert.Contains(block, container);

        container.Insert(0, new ParagraphBlock());
        Assert.Contains(block, container);
    }

    [Fact]
    public void Remove()
    {
        var container = new MockContainerBlock();
        var block = new ParagraphBlock();

        Assert.False(container.Remove(block));
        Assert.Empty(container);
        Assert.Throws<ArgumentOutOfRangeException>(() => container.RemoveAt(0));
        Assert.Empty(container);

        container.Add(block);
        Assert.HasCount(1, container);
        Assert.True(container.Remove(block));
        Assert.Empty(container);
        Assert.False(container.Remove(block));
        Assert.Empty(container);

        container.Add(block);
        Assert.HasCount(1, container);
        container.RemoveAt(0);
        Assert.Empty(container);
        Assert.Throws<ArgumentOutOfRangeException>(() => container.RemoveAt(0));
        Assert.Empty(container);

        container.Add(new ParagraphBlock { Column = 1 });
        container.Add(new ParagraphBlock { Column = 2 });
        container.Add(new ParagraphBlock { Column = 3 });
        container.Add(new ParagraphBlock { Column = 4 });
        Assert.HasCount(4, container);

        container.RemoveAt(2);
        Assert.HasCount(3, container);
        Assert.Equal(4, container[2].Column);

        Assert.True(container.Remove(container[1]));
        Assert.HasCount(2, container);
        Assert.Equal(1, container[0].Column);
        Assert.Equal(4, container[1].Column);
        Assert.Throws<IndexOutOfRangeException>(() => _ = container[2]);
    }

    [Fact]
    public void CopyTo()
    {
        var container = new MockContainerBlock();

        var destination = new Block[4];
        container.CopyTo(destination, 0);
        container.CopyTo(destination, 1);
        container.CopyTo(destination, -1);
        container.CopyTo(destination, 5);
        Assert.Null(destination[0]);

        container.Add(new ParagraphBlock());
        container.CopyTo(destination, 0);
        Assert.NotNull(destination[0]);
        Assert.Null(destination[1]);
        Assert.Null(destination[2]);
        Assert.Null(destination[3]);

        container.CopyTo(destination, 2);
        Assert.NotNull(destination[0]);
        Assert.Null(destination[1]);
        Assert.NotNull(destination[2]);
        Assert.Null(destination[3]);

        Array.Clear(destination);

        container.Add(new ParagraphBlock());
        container.CopyTo(destination, 1);
        Assert.Null(destination[0]);
        Assert.NotNull(destination[1]);
        Assert.NotNull(destination[2]);
        Assert.Null(destination[3]);

        Assert.Throws<IndexOutOfRangeException>(() => container.CopyTo(destination, 3));
    }

    [Fact]
    public void CanTransferChildrenToAnotherContainer()
    {
        var source = new MockContainerBlock();
        var first = new ParagraphBlock { Column = 1 };
        var second = new ParagraphBlock { Column = 2 };
        source.Add(first);
        source.Add(second);

        var destination = new MockContainerBlock();
        var existing = new ParagraphBlock { Column = 0 };
        destination.Add(existing);

        source.TransferChildrenTo(destination);

        Assert.Empty(source);
        Assert.HasCount(3, destination);
        Assert.Same(existing, destination[0]);
        Assert.Same(first, destination[1]);
        Assert.Same(second, destination[2]);
        Assert.Same(destination, first.Parent);
        Assert.Same(destination, second.Parent);
    }

    [Fact]
    public void BlockCanBeRemovedAndReplaced()
    {
        var root = new MockContainerBlock();
        var toRemove = new ParagraphBlock();
        root.Add(toRemove);

        toRemove.Remove();
        Assert.Empty(root);
        Assert.Null(toRemove.Parent);

        var sourceContainer = new MockContainerBlock();
        sourceContainer.Add(new ParagraphBlock { Column = 3 });
        sourceContainer.Add(new ParagraphBlock { Column = 4 });
        root.Add(sourceContainer);

        var replacement = new MockContainerBlock();
        sourceContainer.ReplaceBy(replacement);

        Assert.Same(replacement, root[0]);
        Assert.Null(sourceContainer.Parent);
        Assert.Empty(sourceContainer);
        Assert.HasCount(2, replacement);
        Assert.Equal(3, replacement[0].Column);
        Assert.Equal(4, replacement[1].Column);
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void RandomInsertionsAndRemovalsKeepTheOrderOfTheChildren()
    {
        var random = new Random(42);
        var container = new MockContainerBlock();
        var expected = new List<Block>();
        for (var i = 0; i < 5_000; i++)
        {
            var operation = random.Next(20);
            if (operation < 7)
            {
                var block = new ParagraphBlock { Column = i };
                var index = random.Next(expected.Count + 1);
                container.Insert(index, block);
                expected.Insert(index, block);
            }
            else if (operation < 11)
            {
                var block = new ParagraphBlock { Column = i };
                container.Add(block);
                expected.Add(block);
            }
            else if (operation < 17 && expected.Count > 0)
            {
                var index = random.Next(expected.Count);
                var removed = expected[index];
                container.RemoveAt(index);
                expected.RemoveAt(index);
                Assert.Null(removed.Parent);
            }
            else if (operation < 18 && expected.Count > 0)
            {
                var block = new ParagraphBlock { Column = i };
                var index = random.Next(expected.Count);
                var replaced = expected[index];
                container[index] = block;
                expected[index] = block;
                Assert.Null(replaced.Parent);
            }
            else if (operation < 19)
            {
                container.Sort((a, b) => a.Column.CompareTo(b.Column));
                expected.Sort((a, b) => a.Column.CompareTo(b.Column));
            }
            else if (random.Next(50) == 0)
            {
                container.Clear();
                expected.Clear();
            }

            Assert.HasCount(expected.Count, container);
            Assert.Equal(expected.Count, CountStoredBlocks(container));
            Assert.Same(expected.LastOrDefault(), container.LastChild);
            if (expected.Count > 0)
            {
                var index = random.Next(expected.Count);
                Assert.Equal(index, container.IndexOf(expected[index]));
            }
        }

        Assert.Equal(expected, container.ToList());
        foreach (var block in container)
        {
            Assert.Same(container, block.Parent);
        }

        var copy = new Block[container.Count];
        container.CopyTo(copy, 0);
        Assert.Equal(expected, copy);

        var destination = new MockContainerBlock();
        container.TransferChildrenTo(destination);
        Assert.Empty(container);
        Assert.Equal(expected, destination.ToList());
    }

    // The free slots of the array must not keep references to removed or moved blocks
    private static int CountStoredBlocks(ContainerBlock container)
    {
        var children = (BlockWrapper[])typeof(ContainerBlock).GetField("_children", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(container)!;
        return children.Count(child => child.Block is not null);
    }

    [Fact]
    public void BlocksInsertedOneAfterTheOtherInTheMiddleKeepTheirOrder()
    {
        var container = new MockContainerBlock();
        var expected = new List<Block>();
        for (var i = 0; i < 10; i++)
        {
            var block = new ParagraphBlock { Column = i };
            container.Add(block);
            expected.Add(block);
        }

        // Inserts two blocks after each of the first blocks, like a table followed by a paragraph
        for (var i = 0; i < expected.Count && i < 30; i += 3)
        {
            for (var j = 1; j <= 2; j++)
            {
                var block = new ParagraphBlock { Column = 100 + i + j };
                container.Insert(i + j, block);
                expected.Insert(i + j, block);
            }
        }

        container.Add(new ParagraphBlock { Column = 1000 });
        expected.Add(container.LastChild!);

        Assert.Equal(expected, container.ToList());
        Assert.Equal(expected.Count - 1, container.IndexOf(container.LastChild!));
        Assert.Equal(expected.Count - 1, container.LastIndexOf(container.LastChild!));
    }
}
