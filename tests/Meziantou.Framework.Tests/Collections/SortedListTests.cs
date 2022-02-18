using FluentAssertions;
using Meziantou.Framework.Collections;
using Xunit;

namespace Meziantou.Framework.Tests.Collections;

public sealed class SortedListTests
{
    [Fact]
    public void Test()
    {
        var list = new SortedList<int> { 1, 3, 2, 1 };
        list.Should().Equal(new[] { 1, 1, 2, 3 });

        list.Contains(1).Should().BeTrue();
        list.Contains(42).Should().BeFalse();

        list.IndexOf(1).Should().Be(1);
        list.IndexOf(2).Should().Be(2);
        list.IndexOf(42).Should().Be(-1);

        list.FirstIndexOf(1).Should().Be(0);
        list.LastIndexOf(1).Should().Be(1);

        list.Remove(2);
        list.Should().Equal(new[] { 1, 1, 3 });

        list.Remove(1);
        list.Should().Equal(new[] { 1, 3 });

        list.Remove(1);
        list.Remove(3);
        list.Should().BeEmpty();
    }

    [Fact]
    public void IndexOf()
    {
        var list = new SortedList<int> { 1, 2, 2, 2, 3 };
        list.IndexOf(1).Should().Be(0);
        list.FirstIndexOf(1).Should().Be(0);
        list.LastIndexOf(1).Should().Be(0);

        list.IndexOf(2).Should().Be(2);
        list.FirstIndexOf(2).Should().Be(1);
        list.LastIndexOf(2).Should().Be(3);

        list.IndexOf(3).Should().Be(4);
        list.FirstIndexOf(3).Should().Be(4);
        list.LastIndexOf(3).Should().Be(4);

        list.IndexOf(42).Should().Be(-1);
        list.FirstIndexOf(42).Should().Be(-1);
        list.LastIndexOf(42).Should().Be(-1);
    }

    [Fact]
    public void Clear()
    {
        var list = new SortedList<int> { 1, 3, 2, 1 };
        list.Should().Equal(new[] { 1, 1, 2, 3 });

        list.Clear();
        list.Should().BeEmpty();
    }

    [Fact]
    public void Capacity()
    {
        var list = new SortedList<int> { 1, 3, 2, 1 };
        list.Should().Equal(new[] { 1, 1, 2, 3 });
        list.Capacity.Should().Be(4);
        list.Add(5);

        list.Capacity.Should().Be(8);
    }

    [Fact]
    public void AsSpan()
    {
        var list = new SortedList<int> { 1, 3, 2 };

        list.UnsafeAsReadOnlySpan().Length.Should().Be(3);
    }
}
