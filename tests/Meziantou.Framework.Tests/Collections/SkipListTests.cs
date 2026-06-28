using Meziantou.Framework.Collections;

namespace Meziantou.Framework.Tests.Collections;

public sealed class SkipListTests
{
    [Fact]
    public void Add_SortsValues_AndRemovesDuplicates()
    {
        var list = new SkipList<int> { 3, 1, 2, 1, 3 };

        Assert.Equal([1, 2, 3], list);
        Assert.HasCount(3, list);
    }

    [Fact]
    public void Add_DuplicateValue_ReturnsFalse()
    {
        var list = new SkipList<int>();

        Assert.True(list.Add(1));
        Assert.False(list.Add(1));
        Assert.Equal([1], list);
    }

    [Fact]
    public void ConstructorFromEnumerable_UsesSetSemantics()
    {
        var list = new SkipList<int>([5, 1, 5, 2, 1]);

        Assert.Equal([1, 2, 5], list);
        Assert.HasCount(3, list);
    }

    [Fact]
    public void Remove()
    {
        var list = new SkipList<int> { 1, 2, 3 };

        Assert.True(list.Remove(2));
        Assert.False(list.Remove(2));
        Assert.Equal([1, 3], list);
    }

    [Fact]
    public void Contains()
    {
        var list = new SkipList<int> { 1, 2, 3 };

        Assert.Contains(2, list);
        Assert.DoesNotContain(42, list);
    }

    [Fact]
    public void TryGetValue()
    {
        var list = new SkipList<string>(StringComparer.OrdinalIgnoreCase) { "value" };

        Assert.True(list.TryGetValue("VALUE", out var actualValue));
        Assert.Equal("value", actualValue);
        Assert.False(list.TryGetValue("missing", out _));
    }

    [Fact]
    public void CopyTo()
    {
        var list = new SkipList<int> { 3, 1, 2 };
        var array = new int[5];

        list.CopyTo(array, 1);

        Assert.Equal([0, 1, 2, 3, 0], array);
    }

    [Fact]
    public void Clear()
    {
        var list = new SkipList<int> { 1, 2, 3 };

        list.Clear();

        Assert.Empty(list);
        Assert.DoesNotContain(1, list);
    }

    [Fact]
    public void EnumeratorBasicIteration()
    {
        var list = new SkipList<int> { 3, 1, 2 };

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public void EnumeratorOnEmptyList()
    {
        var list = new SkipList<int>();

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Empty(result);
    }

    [Fact]
    public void EnumeratorModificationThrowsException()
    {
        var list = new SkipList<int> { 1, 2, 3 };

        var enumerator = list.GetEnumerator();
        enumerator.MoveNext();
        list.Add(4);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void EnumeratorResetAfterModificationThrowsException()
    {
        var list = new SkipList<int> { 1, 2 };

        var enumerator = (System.Collections.IEnumerator)list.GetEnumerator();
        enumerator.MoveNext();
        list.Remove(1);

        Assert.Throws<InvalidOperationException>(() => enumerator.Reset());
    }

    [Fact]
    public void EnumeratorCurrentBeforeMoveNextThrowsException()
    {
        var list = new SkipList<int> { 1 };

        var enumerator = (System.Collections.IEnumerator)list.GetEnumerator();

        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
    }

    [Fact]
    public void EnumeratorCurrentAfterEndThrowsException()
    {
        var list = new SkipList<int> { 1 };

        var enumerator = (System.Collections.IEnumerator)list.GetEnumerator();
        while (enumerator.MoveNext())
        {
        }

        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
    }

    [Fact]
    public void CustomComparer()
    {
        var list = new SkipList<int>(Comparer<int>.Create((a, b) => b.CompareTo(a))) { 1, 3, 2 };

        Assert.Equal([3, 2, 1], list.ToArray());
    }
}
