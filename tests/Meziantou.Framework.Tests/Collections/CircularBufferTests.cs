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
}
