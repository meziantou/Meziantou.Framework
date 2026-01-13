using Meziantou.Framework.Collections;

namespace Meziantou.Framework.Tests.Collections;

public sealed class SortedListTests
{
    [Fact]
    public void Test()
    {
        var list = new SortedList<int> { 1, 3, 2, 1 };
        Assert.Equal([1, 1, 2, 3], list);
        Assert.Contains(1, list);
        Assert.DoesNotContain(42, list);
        Assert.Equal(1, list.IndexOf(1));
        Assert.Equal(2, list.IndexOf(2));
        Assert.Equal(-1, list.IndexOf(42));
        Assert.Equal(0, list.FirstIndexOf(1));
        Assert.Equal(1, list.LastIndexOf(1));

        list.Remove(2);
        Assert.Equal([1, 1, 3], list);

        list.Remove(1);
        Assert.Equal([1, 3], list);

        list.Remove(1);
        list.Remove(3);
        Assert.Empty(list);
    }

    [Fact]
    public void IndexOf()
    {
        var list = new SortedList<int> { 1, 2, 2, 2, 3 };
        Assert.Equal(0, list.IndexOf(1));
        Assert.Equal(0, list.FirstIndexOf(1));
        Assert.Equal(0, list.LastIndexOf(1));
        Assert.Equal(2, list.IndexOf(2));
        Assert.Equal(1, list.FirstIndexOf(2));
        Assert.Equal(3, list.LastIndexOf(2));
        Assert.Equal(4, list.IndexOf(3));
        Assert.Equal(4, list.FirstIndexOf(3));
        Assert.Equal(4, list.LastIndexOf(3));
        Assert.Equal(-1, list.IndexOf(42));
        Assert.Equal(-1, list.FirstIndexOf(42));
        Assert.Equal(-1, list.LastIndexOf(42));
    }

    [Fact]
    public void Clear()
    {
        var list = new SortedList<int> { 1, 3, 2, 1 };
        Assert.Equal([1, 1, 2, 3], list);

        list.Clear();
        Assert.Empty(list);
    }

    [Fact]
    public void Capacity()
    {
        var list = new SortedList<int> { 1, 3, 2, 1 };
        Assert.Equal([1, 1, 2, 3], list);
        Assert.Equal(4, list.Capacity);
        list.Add(5);
        Assert.Equal(8, list.Capacity);
    }

    [Fact]
    public void AsSpan()
    {
        var list = new SortedList<int> { 1, 3, 2 };
        Assert.Equal(3, list.UnsafeAsReadOnlySpan().Length);
    }

    [Fact]
    public void EnumeratorBasicIteration()
    {
        var list = new SortedList<int> { 3, 1, 2 };

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
        var list = new SortedList<int>();

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Empty(result);
    }

    [Fact]
    public void EnumeratorAfterRemove()
    {
        var list = new SortedList<int> { 1, 2, 3, 4 };
        list.Remove(2);

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([1, 3, 4], result);
    }

    [Fact]
    public void EnumeratorAfterRemoveAt()
    {
        var list = new SortedList<int> { 1, 2, 3, 4 };
        list.RemoveAt(1);

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([1, 3, 4], result);
    }

    [Fact]
    public void EnumeratorWithDuplicates()
    {
        var list = new SortedList<int> { 2, 1, 2, 3, 1 };

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([1, 1, 2, 2, 3], result);
    }

    [Fact]
    public void EnumeratorResetRestartsIteration()
    {
        var list = new SortedList<int> { 3, 1, 2 };

        var enumerator = (System.Collections.IEnumerator)list.GetEnumerator();
        enumerator.MoveNext();
        enumerator.MoveNext();

        enumerator.Reset();

        var result = new List<int>();
        while (enumerator.MoveNext())
        {
            result.Add((int)enumerator.Current);
        }

        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public void EnumeratorResetMultipleTimes()
    {
        var list = new SortedList<int> { 2, 1 };

        var enumerator = (System.Collections.IEnumerator)list.GetEnumerator();
        enumerator.MoveNext();
        enumerator.Reset();
        enumerator.MoveNext();
        Assert.Equal(1, enumerator.Current);

        enumerator.Reset();
        enumerator.MoveNext();
        Assert.Equal(1, enumerator.Current);
    }

    [Fact]
    public void EnumeratorModificationThrowsException()
    {
        var list = new SortedList<int> { 1, 2, 3 };

        var enumerator = list.GetEnumerator();
        enumerator.MoveNext();

        list.Add(4);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void EnumeratorResetAfterModificationThrowsException()
    {
        var list = new SortedList<int> { 1, 2 };

        var enumerator = (System.Collections.IEnumerator)list.GetEnumerator();
        enumerator.MoveNext();

        list.Add(3);

        Assert.Throws<InvalidOperationException>(() => enumerator.Reset());
    }

    [Fact]
    public void EnumeratorCurrentBeforeMoveNextThrowsException()
    {
        var list = new SortedList<int> { 1 };

        var enumerator = (System.Collections.IEnumerator)list.GetEnumerator();

        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
    }

    [Fact]
    public void EnumeratorCurrentAfterEndThrowsException()
    {
        var list = new SortedList<int> { 1 };

        var enumerator = (System.Collections.IEnumerator)list.GetEnumerator();
        while (enumerator.MoveNext()) { }

        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
    }

    [Fact]
    public void EnumeratorAfterClear()
    {
        var list = new SortedList<int> { 1, 2, 3 };
        list.Clear();

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Empty(result);
    }

    [Fact]
    public void EnumeratorGenericAndNonGeneric()
    {
        var list = new SortedList<int> { 2, 1 };

        var genericResult = new List<int>();
        using var genericEnumerator = ((IEnumerable<int>)list).GetEnumerator();
        while (genericEnumerator.MoveNext())
        {
            genericResult.Add(genericEnumerator.Current);
        }

        var nonGenericResult = new List<int>();
        var nonGenericEnumerator = ((System.Collections.IEnumerable)list).GetEnumerator();
        while (nonGenericEnumerator.MoveNext())
        {
            nonGenericResult.Add((int)nonGenericEnumerator.Current);
        }

        Assert.Equal(genericResult, nonGenericResult);
        Assert.Equal([1, 2], genericResult);
    }

    [Fact]
    public void EnumeratorDisposeIsNoOp()
    {
        var list = new SortedList<int> { 1, 2 };

        var enumerator = list.GetEnumerator();
        enumerator.MoveNext();
        enumerator.Dispose();
        enumerator.Dispose();
    }

    [Fact]
    public void EnumeratorMultipleIterations()
    {
        var list = new SortedList<int> { 3, 1, 2 };

        var firstPass = new List<int>();
        foreach (var item in list)
        {
            firstPass.Add(item);
        }

        var secondPass = new List<int>();
        foreach (var item in list)
        {
            secondPass.Add(item);
        }

        Assert.Equal([1, 2, 3], firstPass);
        Assert.Equal(firstPass, secondPass);
    }

    [Fact]
    public void EnumeratorWithCustomComparer()
    {
        var list = new SortedList<int>(Comparer<int>.Create((a, b) => b.CompareTo(a))) { 1, 3, 2 };

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([3, 2, 1], result);
    }

    [Fact]
    public void EnumeratorAfterCapacityChange()
    {
        var list = new SortedList<int>(2) { 1, 2 };
        list.Capacity = 10;

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([1, 2], result);
    }

    [Fact]
    public void EnumeratorModificationByRemoveThrowsException()
    {
        var list = new SortedList<int> { 1, 2, 3 };

        var enumerator = list.GetEnumerator();
        enumerator.MoveNext();

        list.Remove(2);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void EnumeratorModificationByClearThrowsException()
    {
        var list = new SortedList<int> { 1, 2, 3 };

        var enumerator = list.GetEnumerator();
        enumerator.MoveNext();

        list.Clear();

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void EnumeratorSingleElement()
    {
        var list = new SortedList<int> { 42 };

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([42], result);
    }
}
