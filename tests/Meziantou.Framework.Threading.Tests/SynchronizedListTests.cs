using Meziantou.Framework.Collections.Concurrent;
using Xunit;

namespace Meziantou.Framework.Tests.Collections.Concurrent;

public sealed class SynchronizedListTests
{
    [Fact]
    public void CollectionInitializer()
    {
        SynchronizedList<int> list = [1, 2, 3];
        Assert.Equal(new[] { 1, 2, 3 }, list);
    }

    [Fact]
    public void CollectionInitializer_Spread()
    {
        SynchronizedList<int> list1 = [1, 2, 3];
        SynchronizedList<int> list2 = [.. list1, 4];
        Assert.Equal(new[] { 1, 2, 3, 4 }, list2);
    }

    [Fact]
    public void DefaultConstructor_ShouldBeEmpty()
    {
        var list = new SynchronizedList<int>();
        Assert.Empty(list);
    }

    [Fact]
    public void ConstructorWithCapacity_ShouldBeEmpty()
    {
        var list = new SynchronizedList<int>(10);
        Assert.Empty(list);
    }

    [Fact]
    public void ConstructorWithEnumerable_ShouldContainItems()
    {
        var items = new[] { 1, 2, 3 };
        var list = new SynchronizedList<int>(items);
        Assert.Equal(items, list);
    }

    [Fact]
    public void ConstructorWithReadOnlySpan_ShouldContainItems()
    {
        var items = new[] { 4, 5, 6 };
        var list = new SynchronizedList<int>(items.AsSpan());
        Assert.Equal(items, list);
    }

    [Fact]
    public void Add_ShouldAddItems()
    {
        var list = new SynchronizedList<int>();
        list.Add(1);
        list.Add(2);
        Assert.Equal(new[] { 1, 2 }, list);
    }

    [Fact]
    public void Remove_ShouldRemoveItem()
    {
        var list = new SynchronizedList<int>([1, 2, 3]);
        var result = list.Remove(2);
        Assert.True(result);
        Assert.Equal(new[] { 1, 3 }, list);
    }

    [Fact]
    public void RemoveAt_ShouldRemoveItemAtIndex()
    {
        var list = new SynchronizedList<int>([1, 2, 3]);
        list.RemoveAt(1);
        Assert.Equal(new[] { 1, 3 }, list);
    }

    [Fact]
    public void Insert_ShouldInsertItem()
    {
        var list = new SynchronizedList<int>([1, 3]);
        list.Insert(1, 2);
        Assert.Equal(new[] { 1, 2, 3 }, list);
    }

    [Fact]
    public void Clear_ShouldRemoveAllItems()
    {
        var list = new SynchronizedList<int>([1, 2, 3]);
        list.Clear();
        Assert.Empty(list);
    }

    [Fact]
    [SuppressMessage("Assertions", "xUnit2017:Do not use Contains() to check if a value exists in a collection")]
    public void Contains_ShouldReturnTrueIfItemExists()
    {
        var list = new SynchronizedList<int>([1, 2, 3]);
        Assert.True(list.Contains(2));
        Assert.False(list.Contains(4));
    }

    [Fact]
    public void IndexOf_ShouldReturnCorrectIndex()
    {
        var list = new SynchronizedList<int>([1, 2, 3]);
        Assert.Equal(1, list.IndexOf(2));
        Assert.Equal(-1, list.IndexOf(4));
    }

    [Fact]
    public void CopyTo_ShouldCopyItems()
    {
        var list = new SynchronizedList<int>([1, 2, 3]);
        var array = new int[5];
        list.CopyTo(array, 1);
        Assert.Equal([0, 1, 2, 3, 0], array);
    }

    [Fact]
    public void EnsureCapacity_ShouldIncreaseCapacity()
    {
        var list = new SynchronizedList<int>();
        var capacity = list.EnsureCapacity(100);
        Assert.True(capacity >= 100);
    }

    [Fact]
    public void Indexer_GetSet_ShouldWork()
    {
        var list = new SynchronizedList<int>([1, 2, 3]);
        Assert.Equal(2, list[1]);
        list[1] = 42;
        Assert.Equal(42, list[1]);
    }

    [Fact]
    public void Enumerator_ShouldReturnAllItems()
    {
        var items = new[] { 1, 2, 3 };
        var list = new SynchronizedList<int>(items);
        Assert.Equal(items, list.ToArray());
    }

    [Fact]
    public void IsReadOnly_ShouldBeFalse()
    {
        var list = new SynchronizedList<int>();
        Assert.False(((ICollection<int>)list).IsReadOnly);
    }

    [Fact]
    public void CopyToArray_ShouldCopyItems()
    {
        var list = new SynchronizedList<int>([1, 2, 3]);
        var array = new int[3];
        list.CopyTo(array, 0);
        Assert.Equal([1, 2, 3], array);
    }

    [Fact]
    public async Task ThreadSafety_AddRemove_ShouldWorkCorrectly()
    {
        var list = new SynchronizedList<int>();
        var tasks = new List<Task>();

        for (var i = 0; i < 100; i++)
        {
            var value = i;
            tasks.Add(Task.Run(() => list.Add(value)));
        }

        await Task.WhenAll(tasks);
        Assert.Equal(100, list.Count);

        tasks.Clear();
        for (var i = 0; i < 100; i++)
        {
            var value = i;
            tasks.Add(Task.Run(() => list.Remove(value)));
        }

        await Task.WhenAll(tasks);
        Assert.Empty(list);
    }
}