using Meziantou.Framework.Collections;
using Xunit;

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
}
