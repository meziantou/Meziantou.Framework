using System.Globalization;
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

    [Fact]
    public void Documentation_Usage()
    {
        #region BTree_Usage
        var tree = new BTree<int>();
        Assert.True(tree.Add(30));
        Assert.True(tree.Add(10));
        Assert.True(tree.Add(20));
        Assert.False(tree.Add(10)); // Already present

        Assert.True(tree.Contains(20));
        Assert.Equal([10, 20, 30], tree);

        Assert.True(tree.Remove(20));
        Assert.False(tree.Remove(20)); // Not present anymore
        Assert.Equal([10, 30], tree);
        #endregion
    }

    [Fact]
    public void AddExistingItemWhileEnumerating_SplitsNodeAndInvalidatesEnumerator()
    {
        var tree = new BTree<int>();
        for (var i = 0; i < 15; i++)
        {
            tree.Add(i * 2);
        }

        var enumerator = tree.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        Assert.False(tree.Add(0));

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        Assert.Equal(Enumerable.Range(0, 15).Select(i => i * 2), tree);
    }

    [Fact]
    public void Remove()
    {
        var tree = new BTree<int>();
        Assert.False(tree.Remove(1));

        tree.Add(1);
        tree.Add(2);
        tree.Add(3);

        Assert.True(tree.Remove(2));
        Assert.False(tree.Remove(2));
        Assert.Equal(2, tree.Count);
        Assert.Equal([1, 3], tree);

        Assert.True(tree.Remove(1));
        Assert.True(tree.Remove(3));
        Assert.Empty(tree);
        Assert.False(tree.Contains(1));

        Assert.True(tree.Add(4));
        Assert.Equal([4], tree);
    }

    [Fact]
    public void Remove_ReferenceType()
    {
        var tree = new BTree<string>(StringComparer.Ordinal);
        for (var i = 0; i < 500; i++)
        {
            tree.Add(i.ToString("D4", CultureInfo.InvariantCulture));
        }

        for (var i = 0; i < 500; i += 2)
        {
            Assert.True(tree.Remove(i.ToString("D4", CultureInfo.InvariantCulture)));
        }

        Assert.Equal(Enumerable.Range(0, 500).Where(i => i % 2 is 1).Select(i => i.ToString("D4", CultureInfo.InvariantCulture)), tree);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic test data")]
    public void AddAndRemove_RandomOperations_MatchesSortedSet(int seed)
    {
        var random = new Random(seed);
        var tree = new BTree<int>();
        var expected = new SortedSet<int>();
        for (var i = 0; i < 20_000; i++)
        {
            var value = random.Next(2_000);
            if (random.Next(3) is 0)
            {
                Assert.Equal(expected.Remove(value), tree.Remove(value));
            }
            else
            {
                Assert.Equal(expected.Add(value), tree.Add(value));
            }

            Assert.Equal(expected.Count, tree.Count);
            if (i % 1000 is 0)
            {
                Assert.Equal(expected, tree);
            }
        }

        Assert.Equal(expected, tree);
        foreach (var value in expected.ToArray())
        {
            Assert.True(tree.Remove(value));
            Assert.False(tree.Contains(value));
        }

        Assert.Equal(0, tree.Count);
        Assert.Empty(tree);
    }

    [Fact]
    public void RemoveWhileEnumerating_InvalidatesEnumerator()
    {
        var tree = new BTree<int>();
        tree.Add(1);
        tree.Add(2);

        var enumerator = tree.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        tree.Remove(2);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void RemoveMissingItemWhileEnumerating_RebalancesAndInvalidatesEnumerator()
    {
        var tree = new BTree<int>();
        for (var i = 0; i < 16; i++)
        {
            tree.Add(i * 2);
        }

        tree.Remove(0);

        var enumerator = tree.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        // The root has one key and two children with the minimum number of keys: they are merged even if the item is missing
        Assert.False(tree.Remove(1));

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        Assert.Equal(Enumerable.Range(1, 15).Select(i => i * 2), tree);
    }

    [Fact]
    public void Clear()
    {
        var tree = new BTree<int>();
        for (var i = 0; i < 100; i++)
        {
            tree.Add(i);
        }

        var enumerator = tree.GetEnumerator();
        tree.Clear();

        Assert.Equal(0, tree.Count);
        Assert.Empty(tree);
        Assert.False(tree.Contains(1));
        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());

        Assert.True(tree.Add(1));
        Assert.Equal([1], tree);
    }
}
