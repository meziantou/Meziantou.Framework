using FluentAssertions;
using Meziantou.Framework.Collections;
using Xunit;

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

        list.Should().Equal(new int[] { 2, 1, 3 });
        list[0].Should().Be(2);
        list[1].Should().Be(1);
        list[2].Should().Be(3);

        list.AddLast(4);
        list.Should().Equal(new int[] { 1, 3, 4 });

        list.RemoveFirst();
        list.Should().Equal(new int[] { 3, 4 });

        list.RemoveLast();
        list.Should().Equal(new int[] { 3 });

        list[0].Should().Be(3);

        list.RemoveLast();
        list.Should().BeEmpty();

        list.AddFirst(1);
        list.Should().Equal(new int[] { 1 });
    }

    [Fact]
    public void Test_AddLast()
    {
        var list = new CircularBuffer<int>(3) { AllowOverwrite = true };

        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);

        list.Should().Equal(new int[] { 1, 2, 3 });
    }

    [Fact]
    public void Test_Size1()
    {
        var list = new CircularBuffer<int>(1) { AllowOverwrite = true };

        list.AddFirst(1);
        list.Should().Equal(new int[] { 1 });

        list.AddFirst(2);
        list.Should().Equal(new int[] { 2 });

        list.AddLast(3);
        list.Should().Equal(new int[] { 3 });
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

        // Assert
        index.Should().Be(1);
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

        // Assert
        index.Should().Be(-1);
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

        // Assert
        count.Should().Be(2);
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

        // Assert
        result.Should().BeTrue();
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

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Capacity1()
    {
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.RemoveFirst();

        list.Capacity = 1;
        list.Should().Equal(new[] { 2 });
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
        list.Should().Equal(new[] { 2, 1 });
    }

    [Fact]
    public void Clear()
    {
        var list = new CircularBuffer<int>(3);
        list.AddLast(1);
        list.AddLast(2);
        list.RemoveFirst();
        list.AddLast(1);
        list.Should().Equal(new[] { 2, 1 });

        list.Clear();
        list.Should().BeEmpty();

        list.AddLast(1);
        list.Should().Equal(new[] { 1 });
    }
}
