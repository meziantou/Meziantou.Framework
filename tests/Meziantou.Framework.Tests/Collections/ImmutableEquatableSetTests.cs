#pragma warning disable CS1718 // Comparison made to same variable
#pragma warning disable xUnit2013 // Do not use equality check to check for collection size.
using Meziantou.Framework.Collections;

namespace Meziantou.Framework.Tests.Collections;

public sealed class ImmutableEquatableSetTests
{
    [Fact]
    public void Empty_ShouldReturnEmptySet()
    {
        var empty = ImmutableEquatableSet<string>.Empty;
        Assert.Equal(0, empty.Count);
    }

    [Fact]
    public void Constructor_ShouldCreateSetWithUniqueItems()
    {
        var values = new HashSet<string>(StringComparer.Ordinal) { "a", "b", "c" };
        var set = ImmutableEquatableSet.Create(values);

        Assert.Equal(3, set.Count);
        Assert.Contains("a", set);
        Assert.Contains("b", set);
        Assert.Contains("c", set);
    }

    [Fact]
    public void Contains_ShouldReturnCorrectResults()
    {
        var values = new HashSet<string>(StringComparer.Ordinal) { "a", "b", "c" };
        var set = ImmutableEquatableSet.Create(values);

        Assert.Contains("a", set);
        Assert.Contains("b", set);
        Assert.Contains("c", set);
        Assert.DoesNotContain("d", set);
        Assert.DoesNotContain(null, set);
    }

    [Fact]
    public void Equals_SameReference_ShouldReturnTrue()
    {
        var set = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b" });

        Assert.True(set.Equals(set));
        Assert.True(set == set);
        Assert.False(set != set);
    }

    [Fact]
    public void Equals_SameValues_ShouldReturnTrue()
    {
        var set1 = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b" });
        var set2 = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "b", "a" }); // Different order

        Assert.True(set1.Equals(set2));
        Assert.True(set1 == set2);
        Assert.False(set1 != set2);
    }

    [Fact]
    public void Equals_DifferentValues_ShouldReturnFalse()
    {
        var set1 = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b" });
        var set2 = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "c" });

        Assert.False(set1.Equals(set2));
        Assert.False(set1 == set2);
        Assert.True(set1 != set2);
    }

    [Fact]
    public void Equals_DifferentCount_ShouldReturnFalse()
    {
        var set1 = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b" });
        var set2 = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a" });

        Assert.False(set1.Equals(set2));
        Assert.False(set1 == set2);
        Assert.True(set1 != set2);
    }

    [Fact]
    public void Equals_WithNull_ShouldReturnFalse()
    {
        var set = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a" });

        Assert.False(set.Equals(null));
        Assert.False(set == null);
        Assert.False(null == set);
        Assert.True(set != null);
        Assert.True(null != set);
    }

    [Fact]
    public void Equals_Object_ShouldWorkCorrectly()
    {
        var set1 = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b" });
        var set2 = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b" });
        object obj = set2;

        Assert.True(set1.Equals(obj));
        Assert.False(set1.Equals("not a set"));
    }

    [Fact]
    public void GetHashCode_ShouldReturnCount()
    {
        var set = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b", "c" });

        Assert.Equal(3, set.GetHashCode());
    }

    [Fact]
    public void GetEnumerator_ShouldEnumerateAllValues()
    {
        var values = new HashSet<string>(StringComparer.Ordinal) { "a", "b", "c" };
        var set = ImmutableEquatableSet.Create(values);
        var result = new HashSet<string>(StringComparer.Ordinal);

        var enumerator = set.GetEnumerator();
        while (enumerator.MoveNext())
        {
            result.Add(enumerator.Current);
        }
        enumerator.Dispose();

        Assert.Equal(values, result);
    }

    [Fact]
    public void IEnumerable_GetEnumerator_ShouldWork()
    {
        var values = new HashSet<string>(StringComparer.Ordinal) { "a", "b", "c" };
        var set = ImmutableEquatableSet.Create(values);
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in (IEnumerable<string>)set)
        {
            result.Add(item);
        }

        Assert.Equal(values, result);
    }

    [Fact]
    public void IEnumerable_NonGeneric_GetEnumerator_ShouldWork()
    {
        var values = new HashSet<string>(StringComparer.Ordinal) { "a", "b", "c" };
        var set = ImmutableEquatableSet.Create(values);
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (string item in (System.Collections.IEnumerable)set)
        {
            result.Add(item);
        }

        Assert.Equal(values, result);
    }

    [Fact]
    public void ICollection_Properties_ShouldBeReadOnly()
    {
        var set = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b" });
        var collection = (ICollection<string>)set;

        Assert.True(collection.IsReadOnly);
        Assert.Equal(2, collection.Count);
    }

    [Fact]
    public void ICollection_NonGeneric_Properties_ShouldWork()
    {
        var set = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b" });
        var collection = (System.Collections.ICollection)set;

        Assert.Equal(2, collection.Count);
        Assert.False(collection.IsSynchronized);
        Assert.Same(set, collection.SyncRoot);
    }

    [Fact]
    public void ICollection_CopyTo_ShouldWork()
    {
        var set = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b", "c" });
        var collection = (ICollection<string>)set;
        var target = new string[5];

        collection.CopyTo(target, 1);

        var copiedItems = new HashSet<string>(StringComparer.Ordinal) { target[1], target[2], target[3] };
        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "a", "b", "c" }, copiedItems);
        Assert.Null(target[0]);
        Assert.Null(target[4]);
    }

    [Fact]
    public void ISet_IsSubsetOf_ShouldWork()
    {
        var set = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b" });
        var iset = (ISet<string>)set;

        Assert.True(iset.IsSubsetOf(new[] { "a", "b", "c" }));
        Assert.True(iset.IsSubsetOf(new[] { "a", "b" }));
        Assert.False(iset.IsSubsetOf(new[] { "a" }));
    }

    [Fact]
    public void ISet_IsSupersetOf_ShouldWork()
    {
        var set = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b", "c" });
        var iset = (ISet<string>)set;

        Assert.True(iset.IsSupersetOf(new[] { "a", "b" }));
        Assert.True(iset.IsSupersetOf(new[] { "a" }));
        Assert.False(iset.IsSupersetOf(new[] { "a", "b", "c", "d" }));
    }

    [Fact]
    public void ISet_IsProperSubsetOf_ShouldWork()
    {
        var set = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b" });
        var iset = (ISet<string>)set;

        Assert.True(iset.IsProperSubsetOf(new[] { "a", "b", "c" }));
        Assert.False(iset.IsProperSubsetOf(new[] { "a", "b" }));
        Assert.False(iset.IsProperSubsetOf(new[] { "a" }));
    }

    [Fact]
    public void ISet_IsProperSupersetOf_ShouldWork()
    {
        var set = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b", "c" });
        var iset = (ISet<string>)set;

        Assert.True(iset.IsProperSupersetOf(new[] { "a", "b" }));
        Assert.False(iset.IsProperSupersetOf(new[] { "a", "b", "c" }));
        Assert.False(iset.IsProperSupersetOf(new[] { "a", "b", "c", "d" }));
    }

    [Fact]
    public void ISet_Overlaps_ShouldWork()
    {
        var set = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b" });
        var iset = (ISet<string>)set;

        Assert.True(iset.Overlaps(new[] { "a", "c" }));
        Assert.True(iset.Overlaps(new[] { "b", "d" }));
        Assert.False(iset.Overlaps(new[] { "c", "d" }));
        Assert.False(iset.Overlaps(Array.Empty<string>()));
    }

    [Fact]
    public void ISet_SetEquals_ShouldWork()
    {
        var set = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b" });
        var iset = (ISet<string>)set;

        Assert.True(iset.SetEquals(new[] { "a", "b" }));
        Assert.True(iset.SetEquals(new[] { "b", "a" })); // Different order
        Assert.False(iset.SetEquals(new[] { "a", "b", "c" }));
        Assert.False(iset.SetEquals(new[] { "a" }));
    }

    [Fact]
    public void MutatingOperations_ShouldThrowInvalidOperationException()
    {
        var set = ImmutableEquatableSet.Create(new HashSet<string>(StringComparer.Ordinal) { "a", "b" });
        var collection = (ICollection<string>)set;
        var iset = (ISet<string>)set;

        Assert.Throws<InvalidOperationException>(() => collection.Add("c"));
        Assert.Throws<InvalidOperationException>(() => collection.Remove("a"));
        Assert.Throws<InvalidOperationException>(() => collection.Clear());
        Assert.Throws<InvalidOperationException>(() => iset.Add("c"));
        Assert.Throws<InvalidOperationException>(() => iset.UnionWith(new[] { "c" }));
        Assert.Throws<InvalidOperationException>(() => iset.IntersectWith(new[] { "a" }));
        Assert.Throws<InvalidOperationException>(() => iset.ExceptWith(new[] { "a" }));
        Assert.Throws<InvalidOperationException>(() => iset.SymmetricExceptWith(new[] { "c" }));
    }

    [Fact]
    public void ToImmutableEquatableSet_EmptyCollection_ShouldReturnEmpty()
    {
        var result = new List<string>().ToImmutableEquatableSet();

        Assert.Same(ImmutableEquatableSet<string>.Empty, result);
    }

    [Fact]
    public void ToImmutableEquatableSet_NonEmptyCollection_ShouldCreateSet()
    {
        var values = new[] { "a", "b", "c", "a" }; // Include duplicate
        var result = values.ToImmutableEquatableSet();

        Assert.Equal(3, result.Count); // Should remove duplicate
        Assert.Contains("a", result);
        Assert.Contains("b", result);
        Assert.Contains("c", result);
    }

    [Fact]
    public void ToImmutableEquatableSet_EmptyArray_ShouldReturnEmpty()
    {
        var result = Array.Empty<string>().ToImmutableEquatableSet();

        Assert.Same(ImmutableEquatableSet<string>.Empty, result);
    }

    [Fact]
    public void Create_EmptySpan_ShouldReturnEmpty()
    {
        var result = ImmutableEquatableSet.Create(ReadOnlySpan<string>.Empty);

        Assert.Same(ImmutableEquatableSet<string>.Empty, result);
    }

    [Fact]
    public void Create_NonEmptySpan_ShouldCreateSet()
    {
        var values = new[] { "a", "b", "c", "a" }; // Include duplicate
        var result = ImmutableEquatableSet.Create(values.AsSpan());

        Assert.Equal(3, result.Count); // Should remove duplicate
        Assert.Contains("a", result);
        Assert.Contains("b", result);
        Assert.Contains("c", result);
    }

    [Fact]
    public void CollectionBuilder_ShouldWork()
    {
        ImmutableEquatableSet<string> set = ["a", "b", "c", "a"]; // Include duplicate

        Assert.Equal(3, set.Count); // Should remove duplicate
        Assert.Contains("a", set);
        Assert.Contains("b", set);
        Assert.Contains("c", set);
    }

    [Fact]
    public void EmptySet_Operations_ShouldWork()
    {
        var empty = ImmutableEquatableSet<string>.Empty;
        var iset = (ISet<string>)empty;

        Assert.Equal(0, empty.Count);
        Assert.DoesNotContain("anything", empty);
        Assert.True(iset.IsSubsetOf(new[] { "a", "b" }));
        Assert.False(iset.IsSupersetOf(new[] { "a" }));
        Assert.False(iset.Overlaps(new[] { "a", "b" }));
        Assert.True(iset.SetEquals(Array.Empty<string>()));
    }
}