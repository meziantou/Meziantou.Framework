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

    [Fact]
    public void ImplementsSetInterfaces()
    {
        var set = new ConcurrentHashSet<int>();

        Assert.IsAssignableTo<ISet<int>>(set);
        Assert.IsAssignableTo<IReadOnlySet<int>>(set);
    }

    [Fact]
    public void SetComparisonMethods_UseEqualityComparer()
    {
        var set = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);
        set.AddRange("a", "b");

        Assert.True(set.IsSubsetOf(["A", "B", "C"]));
        Assert.True(set.IsProperSubsetOf(["A", "B", "C"]));
        Assert.True(set.IsSupersetOf(["A"]));
        Assert.True(set.IsProperSupersetOf(["A"]));
        Assert.True(set.Overlaps(["A", "C"]));
        Assert.True(set.SetEquals(["A", "B"]));
    }

    [Fact]
    public void SetMutationMethods_UseEqualityComparer()
    {
        var set = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);
        set.AddRange("a", "b");

        set.UnionWith(["B", "c"]);
        Assert.Equal(["a", "b", "c"], set.Order(StringComparer.OrdinalIgnoreCase));

        set.IntersectWith(["A", "C"]);
        Assert.Equal(["a", "c"], set.Order(StringComparer.OrdinalIgnoreCase));

        set.SymmetricExceptWith(["A", "d", "D"]);
        Assert.Equal(["c", "d"], set.Order(StringComparer.OrdinalIgnoreCase));

        set.ExceptWith(["C"]);
        Assert.Equal(["d"], set);
    }
}
