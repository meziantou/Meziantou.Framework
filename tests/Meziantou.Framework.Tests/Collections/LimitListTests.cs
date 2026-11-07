using Meziantou.Framework.Collections;

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

    [Fact]
    public void Documentation_Usage()
    {
        #region LimitList_Usage
        // Keep the 3 most recent items
        var recentFiles = new LimitList<string>(3);
        recentFiles.AddFirst("a.txt");
        recentFiles.AddFirst("b.txt");
        recentFiles.AddFirst("c.txt");
        recentFiles.AddFirst("d.txt"); // a.txt, the last item, is removed

        Assert.Equal(["d.txt", "c.txt", "b.txt"], recentFiles);
        #endregion
    }

    [Fact]
    public void ImplementsIList()
    {
        IList<int> list = new LimitList<int>(3);
        list.Add(1);
        list.Add(2);
        list.Insert(0, 0);
        Assert.Equal([0, 1, 2], list);
        Assert.Equal(1, list.IndexOf(1));

        IReadOnlyList<int> readOnlyList = (LimitList<int>)list;
        Assert.Equal(2, readOnlyList[2]);
    }

    [Theory]
    [InlineData(0, new[] { 9, 1, 2, 3 })]
    [InlineData(1, new[] { 1, 9, 2, 3 })]
    [InlineData(2, new[] { 1, 2, 9, 3 })]
    [InlineData(3, new[] { 1, 2, 3, 9 })]
    public void Insert_NotFull(int index, int[] expected)
    {
        var list = new LimitList<int>(5);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);

        list.Insert(index, 9);
        Assert.Equal(expected, list);
    }

    [Theory]
    [InlineData(0, new[] { 9, 1, 2 })]
    [InlineData(1, new[] { 1, 9, 2 })]
    [InlineData(2, new[] { 1, 2, 9 })]
    [InlineData(3, new[] { 2, 3, 9 })]
    public void Insert_Full(int index, int[] expected)
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);

        list.Insert(index, 9);
        Assert.Equal(expected, list);
    }

    [Fact]
    public void Insert_OutOfRange_Throws()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        Assert.Throws<ArgumentOutOfRangeException>("index", () => list.Insert(2, 9));
        Assert.Throws<ArgumentOutOfRangeException>("index", () => list.Insert(-1, 9));
    }

    [Fact]
    public void Wrapped_IndexerRemoveAtAndRemove()
    {
        var list = new LimitList<int>(4);
        for (var i = 1; i <= 7; i++)
        {
            list.AddLast(i);
        }

        // The underlying buffer is wrapped: [4, 5, 6, 7]
        Assert.Equal([4, 5, 6, 7], list);
        Assert.Equal(6, list[2]);

        list[1] = 50;
        Assert.Equal([4, 50, 6, 7], list);

        list.RemoveAt(2);
        Assert.Equal([4, 50, 7], list);

        Assert.True(list.Remove(4));
        Assert.False(list.Remove(4));
        Assert.Equal([50, 7], list);

        list.AddFirst(1);
        list.AddFirst(0);
        list.AddFirst(-1);
        Assert.Equal([-1, 0, 1, 50], list);
    }

    [Fact]
    public void RemoveAt_AllPositions()
    {
        for (var index = 0; index < 5; index++)
        {
            var list = new LimitList<int>(5);
            for (var i = 0; i < 8; i++)
            {
                list.AddLast(i);
            }

            var expected = new List<int> { 3, 4, 5, 6, 7 };
            expected.RemoveAt(index);
            list.RemoveAt(index);
            Assert.Equal(expected, list);
        }
    }

    [Fact]
    public void Enumerator_IsStruct_AndDetectsModification()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);

        LimitList<int>.Enumerator enumerator = list.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Equal(1, enumerator.Current);
        list.AddLast(3);
        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void Enumerator_Reset()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);

        IEnumerator<int> enumerator = list.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.True(enumerator.MoveNext());
        enumerator.Reset();
        Assert.True(enumerator.MoveNext());
        Assert.Equal(1, enumerator.Current);
    }

    [Fact]
    public void RemoveFirst_Empty_Throws()
    {
        var list = new LimitList<int>(3);
        Assert.Throws<InvalidOperationException>(list.RemoveFirst);
        Assert.Throws<InvalidOperationException>(list.RemoveLast);
    }

    [Fact]
    public void CopyTo_DestinationTooSmall_Throws()
    {
        var list = new LimitList<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        Assert.Throws<ArgumentException>("array", () => list.CopyTo(new int[1], 0));
    }
}
