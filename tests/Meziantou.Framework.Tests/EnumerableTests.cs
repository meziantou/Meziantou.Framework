using System.Collections.Concurrent;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public class EnumerableTests
{
    [Fact]
    public void ReplaceTests_01()
    {
        // Arrange
        var list = new List<int>() { 1, 2, 3 };

        // Act
        list.Replace(2, 5);
        Assert.Equal(new List<int> { 1, 5, 3 }, list);
    }

    [Fact]
    public void ReplaceTests_02()
    {
        // Arrange
        var list = new List<int>() { 1, 2, 3 };

        // Act
        new Action(() => list.Replace(10, 5)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddOrReplaceTests_01()
    {
        // Arrange
        var list = new List<int>() { 1, 2, 3 };

        // Act
        list.AddOrReplace(10, 5);
        Assert.Equal([1, 2, 3, 5], list);
    }

    [Fact]
    public void AddOrReplaceTests_02()
    {
        // Arrange
        var list = new List<string>();

        // Act
        list.AddOrReplace(null, "");
        Assert.Equal([""], list);
    }

    [Fact]
    public void AddOrReplaceTests_03()
    {
        // Arrange
        var list = new List<int> { 1, 2, 3 };

        // Act
        list.AddOrReplace(2, 5);
        Assert.Equal([1, 5, 3], list);
    }

    [Fact]
    public async Task ForEachAsync()
    {
        var bag = new ConcurrentBag<int>();
        await Enumerable.Range(1, 100).ForEachAsync(async i =>
        {
            await Task.Yield();
            bag.Add(i);
        });

        bag.Should().HaveCount(100);
    }

    [Fact]
    public async Task ParallelForEachAsync()
    {
        var bag = new ConcurrentBag<int>();
        await Enumerable.Range(1, 100).ParallelForEachAsync(async i =>
        {
            await Task.Yield();
            bag.Add(i);
        });

        bag.Should().HaveCount(100);
    }

    [Fact]
    public void TimeSpan_Sum()
    {
        // Arrange
        var list = new List<TimeSpan>() { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(20) };

        // Act
        var sum = list.Sum();
        Assert.Equal(TimeSpan.FromSeconds(23), sum);
    }

    [Fact]
    public void TimeSpan_Average()
    {
        // Arrange
        var list = new List<TimeSpan>() { TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20) };

        // Act
        var sum = list.Average();
        Assert.Equal(TimeSpan.FromSeconds(9), sum);
    }

    [Fact]
    public void EmptyIfNull_Null()
    {
        IEnumerable<string> items = null;
        Assert.Equal([], items.EmptyIfNull());
    }

    [Fact]
    public void EmptyIfNull_NotNull()
    {
        var items = new string[] { "" };
        items.EmptyIfNull().Should().BeSameAs(items);
    }

#nullable enable
    [Fact]
    [SuppressMessage("Style", "IDE0007:Use implicit type", Justification = "Need to validate the type is non-nullable")]
    public void WhereNotNull()
    {
        // Arrange
        var list = new List<string?>() { "", null, "a" };

        // Act
        // Do not use var, so we can validate the nullable annotations
        List<string> actual = list.WhereNotNull().ToList();
        Assert.Equal(["", "a"], actual);
    }

    [Fact]
    [SuppressMessage("Style", "IDE0007:Use implicit type", Justification = "Need to validate the type is non-nullable")]
    public void WhereNotNull_Struct()
    {
        // Arrange
        var list = new List<int?>() { 0, null, 2 };

        // Act
        // Do not use var, so we can validate the nullable annotations
        List<int> actual = list.WhereNotNull().ToList();
        Assert.Equal([0, 2], actual);
    }
#nullable disable

    [Fact]
    public void ForeachEnumerator()
    {
        var items = new List<int>();
        foreach (var item in CustomEnumerator())
        {
            items.Add(item);
        }

        Assert.Equal([1, 2], items);

        static IEnumerator<int> CustomEnumerator()
        {
            yield return 1;
            yield return 2;
        }
    }

    [Fact]
    public async Task ForeachAsyncEnumerator()
    {
        var items = new List<int>();
        await foreach (var item in CustomEnumerator())
        {
            items.Add(item);
        }

        Assert.Equal([1, 2], items);

        static async IAsyncEnumerator<int> CustomEnumerator()
        {
            await Task.Yield();
            yield return 1;
            yield return 2;
        }
    }

    [Fact]
    public void IsDistinct_MultipleNulls()
    {
        var array = new[] { "a", null, null };
        Assert.False(array.IsDistinct());
    }

    [Fact]
    public void IsDistinct_MultipleIdenticalValues()
    {
        var array = new[] { "a", "b", "a" };
        Assert.False(array.IsDistinct());
    }

    [Fact]
    public void IsDistinct()
    {
        var array = new[] { "a", "b", "c" };
        Assert.True(array.IsDistinct());
    }

    [Fact]
    public async Task ToListAsync()
    {
        var data = await GetDataAsync().ToListAsync();
        Assert.Equal(["a", "b", "c"], data);

        static async Task<IEnumerable<string>> GetDataAsync()
        {
            await Task.Yield();
            return ["a", "b", "c"];
        }
    }

    [Fact]
    public void AsEnumerableOnceTest()
    {
        var data = new[] { "a" }.AsEnumerableOnce();
        _ = data.ToList();
        FluentActions.Invoking(() => data.ToList()).Should().ThrowExactly<InvalidOperationException>();
    }
}
