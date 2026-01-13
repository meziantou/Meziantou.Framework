using Meziantou.Framework.Collections;

namespace Meziantou.Framework.Tests.Collections;

public class CircularBufferTests
{
    [Fact]
    public void Test()
    {
        var list = new CircularBuffer<int>(3) { AllowOverwrite = true };

        list.AddFirst(1);
        list.AddFirst(2);
        list.AddLast(3);
        Assert.Equal([2, 1, 3], list);
        Assert.Equal(2, list[0]);
        Assert.Equal(1, list[1]);
        Assert.Equal(3, list[2]);

        list.AddLast(4);
        Assert.Equal([1, 3, 4], list);

        list.RemoveFirst();
        Assert.Equal([3, 4], list);

        list.RemoveLast();
        Assert.Equal([3], list);
        Assert.Equal(3, list[0]);

        list.RemoveLast();
        Assert.Empty(list);

        list.AddFirst(1);
        Assert.Equal([1], list);
    }

    [Fact]
    public void Test_AddLast()
    {
        var list = new CircularBuffer<int>(3) { AllowOverwrite = true };

        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        Assert.Equal([1, 2, 3], list);
    }

    [Fact]
    public void Test_Size1()
    {
        var list = new CircularBuffer<int>(1) { AllowOverwrite = true };

        list.AddFirst(1);
        Assert.Equal([1], list);

        list.AddFirst(2);
        Assert.Equal([2], list);

        list.AddLast(3);
        Assert.Equal([3], list);
    }

    [Fact]
    public void IndexOf_01()
    {
        // Arrange
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);

        // Act
        var index = list.IndexOf(2);
        Assert.Equal(1, index);
    }

    [Fact]
    public void IndexOf_02()
    {
        // Arrange
        var list = new CircularBuffer<int>(3) { AllowOverwrite = true };
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        list.AddLast(4);

        // Act
        var index = list.IndexOf(1);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void Count_01()
    {
        // Arrange
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);

        // Act
        var count = list.Count;
        Assert.Equal(2, count);
    }

    [Fact]
    public void Contains_01()
    {
        // Arrange
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);

        // Act
        var result = list.Contains(2);
        Assert.True(result);
    }

    [Fact]
    public void Contains_02()
    {
        // Arrange
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);

        // Act
        var result = list.Contains(3);
        Assert.False(result);
    }

    [Fact]
    public void Capacity1()
    {
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.RemoveFirst();

        list.Capacity = 1;
        Assert.Equal([2], list);
    }

    [Fact]
    public void Capacity2()
    {
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.RemoveFirst();
        list.AddLast(1);

        list.Capacity = 2;
        Assert.Equal([2, 1], list);
    }

    [Fact]
    public void Clear()
    {
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.RemoveFirst();
        list.AddLast(1);
        Assert.Equal([2, 1], list);

        list.Clear();
        Assert.Empty(list);

        list.AddLast(1);
        Assert.Equal([1], list);
    }

    [Fact]
    public void RemoveFirst_Class()
    {
        var list = new CircularBuffer<object>(3);
        list.AddLast(new object());
        Assert.NotNull(list.RemoveFirst());
    }

    [Fact]
    public void RemoveLast_Class()
    {
        var list = new CircularBuffer<object>(3);
        list.AddLast(new object());
        Assert.NotNull(list.RemoveLast());
    }

    [Fact]
    public void EnumeratorBasicIteration()
    {
        var list = new CircularBuffer<int>(5);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public void EnumeratorWithWrappedBuffer()
    {
        var list = new CircularBuffer<int>(3) { AllowOverwrite = true };
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        list.AddLast(4);
        list.AddLast(5);

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([3, 4, 5], result);
    }

    [Fact]
    public void EnumeratorOnEmptyBuffer()
    {
        var list = new CircularBuffer<int>(3);

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Empty(result);
    }

    [Fact]
    public void EnumeratorAfterAddFirst()
    {
        var list = new CircularBuffer<int>(5) { AllowOverwrite = true };
        list.AddFirst(3);
        list.AddFirst(2);
        list.AddFirst(1);

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public void EnumeratorAfterRemoveFirst()
    {
        var list = new CircularBuffer<int>(5);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        list.RemoveFirst();

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([2, 3], result);
    }

    [Fact]
    public void EnumeratorAfterRemoveLast()
    {
        var list = new CircularBuffer<int>(5);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        list.RemoveLast();

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([1, 2], result);
    }

    [Fact]
    public void EnumeratorResetRestartsIteration()
    {
        var list = new CircularBuffer<int>(5);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);

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
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);

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
        var list = new CircularBuffer<int>(5);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);

        var enumerator = list.GetEnumerator();
        enumerator.MoveNext();

        list.AddLast(4);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void EnumeratorResetAfterModificationThrowsException()
    {
        var list = new CircularBuffer<int>(5);
        list.AddLast(1);
        list.AddLast(2);

        var enumerator = (System.Collections.IEnumerator)list.GetEnumerator();
        enumerator.MoveNext();

        list.AddLast(3);

        Assert.Throws<InvalidOperationException>(() => enumerator.Reset());
    }

    [Fact]
    public void EnumeratorCurrentBeforeMoveNextThrowsException()
    {
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);

        var enumerator = (System.Collections.IEnumerator)list.GetEnumerator();

        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
    }

    [Fact]
    public void EnumeratorCurrentAfterEndThrowsException()
    {
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);

        var enumerator = (System.Collections.IEnumerator)list.GetEnumerator();
        while (enumerator.MoveNext()) { }

        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
    }

    [Fact]
    public void EnumeratorWithWrappedBufferAndStartIndex()
    {
        var list = new CircularBuffer<int>(3) { AllowOverwrite = true };
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);
        list.RemoveFirst();
        list.AddLast(4);
        list.AddLast(5);

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([3, 4, 5], result);
    }

    [Fact]
    public void EnumeratorGenericAndNonGeneric()
    {
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);

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
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);

        var enumerator = list.GetEnumerator();
        enumerator.MoveNext();
        enumerator.Dispose();
        enumerator.Dispose();
    }

    [Fact]
    public void EnumeratorAfterClear()
    {
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.Clear();

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Empty(result);
    }

    [Fact]
    public void EnumeratorCapacityOne()
    {
        var list = new CircularBuffer<int>(1) { AllowOverwrite = true };
        list.AddLast(1);
        list.AddLast(2);

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item);
        }

        Assert.Equal([2], result);
    }

    [Fact]
    public void EnumeratorMultipleIterations()
    {
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);

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
}
