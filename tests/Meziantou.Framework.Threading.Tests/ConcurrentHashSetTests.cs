using Meziantou.Framework.Collections.Concurrent;

namespace Meziantou.Framework.Tests.Collections;

public class ConcurrentHashSetTests
{
    [Fact]
    public void ConcurrentHashSet_Example()
    {
        #region ConcurrentHashSet_Usage
        var set = new ConcurrentHashSet<int>();
        set.Add(1);
        set.Add(2);
        Assert.Contains(1, set);
        #endregion
    }

    [Fact]
    public void ConcurrentHashSet_ComparerExample()
    {
        #region ConcurrentHashSet_Comparer
        var set = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add returns false when the set already contains an equal value
        Assert.True(set.Add("Alice"));
        Assert.False(set.Add("ALICE"));

        set.AddRange("Bob", "Charlie");
        Assert.HasCount(3, set);
        Assert.True(set.Remove("bob"));
        #endregion
    }

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

#pragma warning disable MFAS0041, MFAS0043 // The test validates the set comparison methods themselves
        Assert.True(set.IsSubsetOf(["A", "B", "C"]));
        Assert.True(set.IsProperSubsetOf(["A", "B", "C"]));
        Assert.True(set.IsSupersetOf(["A"]));
        Assert.True(set.IsProperSupersetOf(["A"]));
#pragma warning restore MFAS0041, MFAS0043
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

    [Fact]
    public void CopyTo_NullArray_Throws()
    {
        var set = new ConcurrentHashSet<int> { 1 };

        Assert.Throws<ArgumentNullException>(() => set.CopyTo(array: null!, 0));
    }

    [Fact]
    public void CopyTo_NegativeIndex_Throws()
    {
        var set = new ConcurrentHashSet<int> { 1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => set.CopyTo(new int[4], -1));
    }

    [Fact]
    public void CopyTo_DestinationTooSmall_ThrowsArgumentExceptionAndLeavesTheArrayUntouched()
    {
        var set = new ConcurrentHashSet<int> { 1, 2, 3 };
        var destination = new int[2];

        Assert.Throws<ArgumentException>(() => set.CopyTo(destination, 0));
        Assert.Equal([0, 0], destination);
    }

    [Fact]
    public void CopyTo_IndexPastTheEnd_ThrowsArgumentException()
    {
        var set = new ConcurrentHashSet<int> { 1 };

        Assert.Throws<ArgumentException>(() => set.CopyTo(new int[2], 4));
    }

    [Fact]
    public void CopyTo_CopiesTheElementsAtTheGivenOffset()
    {
        var set = new ConcurrentHashSet<int> { 1, 2, 3 };
        var destination = new int[5];

        set.CopyTo(destination, 1);

        Assert.Equal(0, destination[0]);
        Assert.Equal(0, destination[4]);
        Assert.Equal([1, 2, 3], destination[1..4].Order());
    }

    [Fact]
    public void Overlaps_StopsEnumeratingOtherAtTheFirstCommonElement()
    {
        var set = new ConcurrentHashSet<int>();
        set.AddRange(1, 2, 3);
        List<int> enumerated = [];

        Assert.True(set.Overlaps(Record([7, 2, 3], enumerated)));
        Assert.Equal([7, 2], enumerated);
    }

    [Fact]
    public void Overlaps_EmptySet_DoesNotEnumerateOther()
    {
        ConcurrentHashSet<int> set = [];
        List<int> enumerated = [];

        Assert.False(set.Overlaps(Record([1, 2, 3], enumerated)));
        Assert.Empty(enumerated);
    }

    [Fact]
    public void Overlaps_NoCommonElement_ReturnsFalse()
    {
        var set = new ConcurrentHashSet<int>();
        set.AddRange(1, 2, 3);

        Assert.False(set.Overlaps([4, 5]));
        Assert.False(set.Overlaps([]));
    }

    [Fact]
    public void IsSupersetOf_StopsEnumeratingOtherAtTheFirstMissingElement()
    {
        var set = new ConcurrentHashSet<int>();
        set.AddRange(1, 2, 3);
        List<int> enumerated = [];

        Assert.False(set.IsSupersetOf(Record([1, 9, 2], enumerated)));
        Assert.Equal([1, 9], enumerated);
    }

    [Fact]
    public void IsSupersetOf_IgnoresDuplicatesInOther()
    {
        var set = new ConcurrentHashSet<int>();
        set.AddRange(1, 2, 3);

        Assert.True(set.IsSupersetOf([1, 1, 2]));
        Assert.True(set.IsSupersetOf([1, 2, 3]));
        Assert.True(set.IsSupersetOf([]));
        Assert.False(set.IsSupersetOf([1, 4]));
    }

    [Fact]
    public void IsSupersetOf_EmptySet()
    {
        ConcurrentHashSet<int> set = [];

        Assert.True(set.IsSupersetOf([]));
        Assert.False(set.IsSupersetOf([1]));
    }

#pragma warning disable MFAS0043, MFAS0044 // The tests validate the set comparison methods themselves
    [Fact]
    public void IsProperSupersetOf_StopsEnumeratingOtherAtTheFirstMissingElement()
    {
        var set = new ConcurrentHashSet<int>();
        set.AddRange(1, 2, 3);
        List<int> enumerated = [];

        Assert.False(set.IsProperSupersetOf(Record([1, 9, 2], enumerated)));
        Assert.Equal([1, 9], enumerated);
    }

    [Fact]
    public void IsProperSupersetOf_IgnoresDuplicatesInOther()
    {
        var set = new ConcurrentHashSet<int>();
        set.AddRange(1, 2, 3);

        Assert.True(set.IsProperSupersetOf([1, 1, 2, 2, 1]));
        Assert.False(set.IsProperSupersetOf([1, 2, 3, 3, 2, 1]));
        Assert.False(set.IsProperSupersetOf([1, 1, 4]));
    }

    [Fact]
    public void IsProperSupersetOf_UsesTheEqualityComparerToCountDistinctElements()
    {
        var set = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);
        set.AddRange("a", "b");

        Assert.True(set.IsProperSupersetOf(["A", "a"]));
        Assert.False(set.IsProperSupersetOf(["A", "b", "B"]));
        Assert.False(set.IsProperSupersetOf(["c"]));
    }

    [Fact]
    public void IsProperSupersetOf_EmptyOther()
    {
        var set = new ConcurrentHashSet<int>();

        Assert.False(set.IsProperSupersetOf([]));
        Assert.False(set.IsProperSupersetOf(Enumerable.Empty<int>()));

        set.Add(1);
        Assert.True(set.IsProperSupersetOf([]));
        Assert.True(set.IsProperSupersetOf(Enumerable.Empty<int>()));
        Assert.True(set.IsProperSupersetOf(new HashSet<int>()));
    }

    [Fact]
    public void IsProperSupersetOf_Self_ReturnsFalse()
    {
        var set = new ConcurrentHashSet<int>();
        Assert.False(set.IsProperSupersetOf(set));

        set.AddRange(1, 2, 3);
        Assert.False(set.IsProperSupersetOf(set));
    }

    [Fact]
    public void IsProperSupersetOf_EqualSets_ReturnFalse()
    {
        var set = new ConcurrentHashSet<int>();
        set.AddRange(1, 2, 3);

        Assert.False(set.IsProperSupersetOf([3, 2, 1]));
        Assert.False(set.IsProperSupersetOf(new HashSet<int>([1, 2, 3])));

        var other = new ConcurrentHashSet<int>();
        other.AddRange(1, 2, 3);
        Assert.False(set.IsProperSupersetOf(other));
    }

    [Fact]
    public void IsProperSupersetOf_ProperSubsets_ReturnTrue()
    {
        var set = new ConcurrentHashSet<int>();
        set.AddRange(1, 2, 3);

        Assert.True(set.IsProperSupersetOf([2]));
        Assert.True(set.IsProperSupersetOf(Record([1, 3], [])));
        Assert.True(set.IsProperSupersetOf(new HashSet<int>([1, 2])));
        Assert.False(set.IsProperSupersetOf(new HashSet<int>([1, 4])));

        var other = new ConcurrentHashSet<int>();
        other.AddRange(1, 2);
        Assert.True(set.IsProperSupersetOf(other));
    }

    [Fact]
    public void IsProperSupersetOf_SetWithADifferentComparer_CountsDistinctElementsWithTheSetComparer()
    {
        var set = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);
        set.AddRange("a", "b", "c");

        // Distinct for the ordinal comparer, but a single element for the set.
        Assert.True(set.IsProperSupersetOf(new HashSet<string>(["a", "A"], StringComparer.Ordinal)));
        Assert.False(set.IsProperSupersetOf(new HashSet<string>(["a", "B", "c", "C"], StringComparer.Ordinal)));
    }
#pragma warning restore MFAS0043, MFAS0044

    private static IEnumerable<T> Record<T>(IEnumerable<T> source, List<T> enumerated)
    {
        foreach (var item in source)
        {
            enumerated.Add(item);
            yield return item;
        }
    }
}
