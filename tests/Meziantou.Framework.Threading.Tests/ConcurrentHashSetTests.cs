using Meziantou.Framework.Collections.Concurrent;

namespace Meziantou.Framework.Tests.Collections;

public class ConcurrentHashSetTests
{
    [Fact]
    public void TestConcurrentHashSet()
    {
        ConcurrentHashSet<int> set = [];
        Assert.True(set.Add(1));
        Assert.True(set.Add(2));
        Assert.True(set.Add(3));
        Assert.False(set.Add(3));

        Assert.Contains(1, set);
        Assert.DoesNotContain(4, set);

        Assert.HasCount(3, set);
        Assert.Equal([1, 2, 3], set.Order());

        set.Clear();
        Assert.Empty(set);

        set.AddRange(4, 5, 6);
        Assert.Equal([4, 5, 6], set.Order());
    }

    [Fact]
    [SuppressMessage("Assertions", "xUnit2017:Do not use Contains() to check if a value exists in a collection", Justification = "Testing the comparer-aware Contains method directly")]
    public void EdgeCases()
    {
        var set = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(set.IsEmpty);

        Assert.True(set.Add("a"));
        Assert.False(set.Add("A")); // comparer treats "a" and "A" as equal
        Assert.Contains("A", set);

        Assert.False(set.Remove("missing"));
        Assert.True(set.Remove("A"));
        Assert.True(set.IsEmpty);
    }

    [Fact]
    public void AddRange_NullAndEmpty_AreNoops()
    {
        var set = new ConcurrentHashSet<int>();
        set.AddRange((IEnumerable<int>?)null);
        set.AddRange(ReadOnlySpan<int>.Empty);
        Assert.True(set.IsEmpty);
    }

    [Fact]
    public void CopyTo_RespectsOffset()
    {
        var set = new ConcurrentHashSet<int>();
        set.AddRange(1, 2, 3);

        var array = new int[5];
        set.CopyTo(array, 1);

        Assert.Equal(0, array[0]);
        Assert.Equal(0, array[4]);
        Assert.Equal([1, 2, 3], array[1..4].Order());
    }
}
