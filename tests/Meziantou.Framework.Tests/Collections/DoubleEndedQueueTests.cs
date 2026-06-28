using Meziantou.Framework.Collections;

namespace Meziantou.Framework.Tests.Collections;

public sealed class DoubleEndedQueueTests
{
    [Fact]
    public void DoesNotAllocateBeforeFirstAdd()
    {
        var list = new DoubleEndedQueue<int>(3);
        var field = typeof(DoubleEndedQueue<int>).GetField("_items", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Null(field.GetValue(list));

        list.AddLast(1);
        Assert.NotNull(field.GetValue(list));
    }

    [Fact]
    public void AddFirstAndAddLast()
    {
        var list = new DoubleEndedQueue<int>(3);
        list.AddFirst(2);
        list.AddFirst(1);
        list.AddLast(3);

        Assert.Equal([1, 2, 3], list);
        Assert.HasCount(3, list);
    }

    [Fact]
    public void RemoveFromBothEnds()
    {
        var list = new DoubleEndedQueue<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);

        Assert.Equal(1, list.RemoveFirst());
        Assert.Equal(3, list.RemoveLast());
        Assert.Equal([2], list);
    }

    [Fact]
    public void ResizeOnAddLast()
    {
        var list = new DoubleEndedQueue<int>(2);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        list.AddLast(4);

        Assert.Equal([1, 2, 3, 4], list);
    }

    [Fact]
    public void ResizeOnAddFirst()
    {
        var list = new DoubleEndedQueue<int>(2);
        list.AddLast(1);
        list.AddLast(2);
        list.AddFirst(0);
        list.AddFirst(-1);

        Assert.Equal([-1, 0, 1, 2], list);
    }

    [Fact]
    public void IndexerWithWrappedStorage()
    {
        var list = new DoubleEndedQueue<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        list.RemoveFirst();
        list.AddLast(4);

        Assert.Equal(2, list[0]);
        Assert.Equal(3, list[1]);
        Assert.Equal(4, list[2]);
    }

    [Fact]
    public void ContainsAndIndexOf()
    {
        var list = new DoubleEndedQueue<int>(3);
        list.AddLast(10);
        list.AddLast(20);
        list.AddLast(30);
        list.RemoveFirst();
        list.AddLast(40);

        Assert.Contains(30, list);
        Assert.Equal(1, list.IndexOf(30));
        Assert.DoesNotContain(10, list);
        Assert.Equal(-1, list.IndexOf(10));
    }

    [Fact]
    public void CopyToWithOffset()
    {
        var list = new DoubleEndedQueue<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        list.RemoveFirst();
        list.AddLast(4);

        var array = new[] { -1, -1, -1, -1, -1, -1, };
        list.CopyTo(array, 2);

        Assert.Equal([-1, -1, 2, 3, 4, -1], array);
    }

    [Fact]
    public void CopyToThrowsWhenArrayTooSmall()
    {
        var list = new DoubleEndedQueue<int>(2);
        list.AddLast(1);
        list.AddLast(2);

        Assert.Throws<ArgumentException>(() => list.CopyTo(new int[1], 0));
    }

    [Fact]
    public void ClearRemovesItems()
    {
        var list = new DoubleEndedQueue<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.Clear();

        Assert.Empty(list);

        list.AddFirst(3);
        Assert.Equal([3], list);
    }

    [Fact]
    public void RemoveFirstThrowsWhenEmpty()
    {
        var list = new DoubleEndedQueue<int>(1);
        Assert.Throws<InvalidOperationException>(() => list.RemoveFirst());
    }

    [Fact]
    public void RemoveLastThrowsWhenEmpty()
    {
        var list = new DoubleEndedQueue<int>(1);
        Assert.Throws<InvalidOperationException>(() => list.RemoveLast());
    }

    [Fact]
    public void ICollectionRemoveThrowsNotSupportedException()
    {
        ICollection<int> list = new DoubleEndedQueue<int>(1);
        list.Add(1);

        Assert.Throws<NotSupportedException>(() => list.Remove(1));
    }

    [Fact]
    public void EnumeratorIteratesInOrder()
    {
        var list = new DoubleEndedQueue<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        list.RemoveFirst();
        list.AddLast(4);

        Assert.Equal([2, 3, 4], list.ToArray());
    }

    [Fact]
    public void EnumeratorModificationThrowsException()
    {
        var list = new DoubleEndedQueue<int>(3);
        list.AddLast(1);
        list.AddLast(2);

        var enumerator = list.GetEnumerator();
        enumerator.MoveNext();
        list.AddLast(3);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void EnumeratorCurrentBeforeMoveNextThrowsException()
    {
        var list = new DoubleEndedQueue<int>(1);
        list.AddLast(1);

        var enumerator = (System.Collections.IEnumerator)list.GetEnumerator();

        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
    }

    [Fact]
    public void EnumeratorCurrentAfterEndThrowsException()
    {
        var list = new DoubleEndedQueue<int>(1);
        list.AddLast(1);

        var enumerator = (System.Collections.IEnumerator)list.GetEnumerator();
        while (enumerator.MoveNext()) { }

        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
    }
}
