using Meziantou.Framework.Collections;
using Xunit;

namespace Meziantou.Framework.Tests.Collections;

public class LimitListTests
{
    [Fact]
    public void AddFirst_01()
    {
        var list = new LimitList<int>(3);
        list.AddFirst(1);
        list.AddFirst(2);
        Assert.Equal([2, 1], list.ToList());
    }

    [Fact]
    public void AddFirst_02()
    {
        var list = new LimitList<int>(3);
        list.AddFirst(1);
        list.AddFirst(2);
        list.AddFirst(3);
        list.AddFirst(4);
        Assert.Equal([4, 3, 2], list.ToList());
    }

    [Fact]
    public void AddLast_01()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        Assert.Equal([1, 2], list.ToList());
    }

    [Fact]
    public void AddLast_02()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        list.AddLast(4);
        Assert.Equal([2, 3, 4], list.ToList());
    }

    [Fact]
    public void IndexOf_01()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        var index = list.IndexOf(2);
        Assert.Equal(1, index);
    }

    [Fact]
    public void IndexOf_02()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        list.AddLast(4);
        var index = list.IndexOf(1);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void Count_01()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        var count = list.Count;
        Assert.Equal(2, count);
    }

    [Fact]
    public void Contains_01()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        var result = list.Contains(2);
        Assert.True(result);
    }

    [Fact]
    public void Contains_02()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        var result = list.Contains(3);
        Assert.False(result);
    }

    [Fact]
    public void Remove_01()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        var result = list.Remove(1);
        Assert.True(result);
        Assert.Equal([2], list);
    }

    [Fact]
    public void Remove_02()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        var result = list.Remove(2);
        Assert.True(result);
        Assert.Equal([1, 3], list);
    }

    [Fact]
    public void Remove_03()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        var result = list.Remove(4);
        Assert.False(result);
        Assert.Equal([1], list);
    }

    [Fact]
    public void Indexer_01()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list[0] = 10;
        Assert.Equal([10], list);
    }

    [Fact]
    public void Indexer_02()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list[1] = 10;
        Assert.Equal([1, 10], list);
    }

    [Fact]
    public void Indexer_03()
    {
        var list = new LimitList<int>(3);
        Assert.Throws<ArgumentOutOfRangeException>(() => list[1] = 10);
    }

    [Fact]
    public void RemoveAt()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        list.RemoveAt(1);
        Assert.Equal([1, 3], list);
    }
}
