using Meziantou.Framework.Collections;

namespace Meziantou.Framework.Tests.Collections;

public sealed class BTreeTests
{
    [Fact]
    public void AddAndEnumerate_SortedUniqueValues()
    {
        var tree = new BTree<int>();
        Assert.True(tree.Add(5));
        Assert.True(tree.Add(2));
        Assert.True(tree.Add(8));
        Assert.True(tree.Add(1));
        Assert.False(tree.Add(2));
        Assert.False(tree.Add(8));

        Assert.Equal(4, tree.Count);
        Assert.Equal([1, 2, 5, 8], tree);
    }

    [Fact]
    public void Contains()
    {
        var tree = new BTree<int>();
        tree.Add(10);
        tree.Add(4);
        tree.Add(15);

        Assert.True(tree.Contains(10));
        Assert.True(tree.Contains(4));
        Assert.True(tree.Contains(15));
        Assert.False(tree.Contains(11));
    }

    [Fact]
    public void Contains_EmptyTree()
    {
        var tree = new BTree<int>();

        Assert.False(tree.Contains(1));
    }

    [Fact]
    public void AddManyItems_TriggersNodeSplits()
    {
        var tree = new BTree<int>();
        for (var i = 0; i < 500; i++)
        {
            Assert.True(tree.Add(i));
        }

        Assert.Equal(500, tree.Count);
        Assert.Equal(Enumerable.Range(0, 500), tree);
    }

    [Fact]
    public void CustomComparer()
    {
        var comparer = Comparer<int>.Create((a, b) => Math.Abs(a).CompareTo(Math.Abs(b)));
        var tree = new BTree<int>(comparer);
        Assert.True(tree.Add(-2));
        Assert.False(tree.Add(2));
        Assert.True(tree.Add(1));

        Assert.Equal(2, tree.Count);
        Assert.True(tree.Contains(2));
        Assert.True(tree.Contains(-1));
    }

    [Fact]
    public void EnumeratorModificationThrowsException()
    {
        var tree = new BTree<int>();
        tree.Add(1);
        tree.Add(2);

        var enumerator = tree.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        tree.Add(3);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void EnumeratorResetAfterModificationThrowsException()
    {
        var tree = new BTree<int>();
        tree.Add(1);
        tree.Add(2);

        var enumerator = (System.Collections.IEnumerator)tree.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        tree.Add(3);

        Assert.Throws<InvalidOperationException>(() => enumerator.Reset());
    }

    [Fact]
    public void EnumeratorCurrentBeforeMoveNextThrowsException()
    {
        var tree = new BTree<int>();
        tree.Add(1);

        var enumerator = (System.Collections.IEnumerator)tree.GetEnumerator();

        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
    }

    [Fact]
    public void EnumeratorCurrentAfterEndThrowsException()
    {
        var tree = new BTree<int>();
        tree.Add(1);

        var enumerator = (System.Collections.IEnumerator)tree.GetEnumerator();
        while (enumerator.MoveNext())
        {
        }

        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
    }
}
