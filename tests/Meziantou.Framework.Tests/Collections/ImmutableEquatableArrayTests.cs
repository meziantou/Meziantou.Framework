#pragma warning disable CS1718 // Comparison made to same variable
using System.Collections.Immutable;
using Meziantou.Framework.Collections;

namespace Meziantou.Framework.Tests.Collections;
public sealed class ImmutableEquatableArrayTests
{
    [Fact]
    public void EquatableImmutableArray_Empty()
    {
        ImmutableArray<int> immutableArray1 = [0, 1, 2];
        ImmutableArray<int> immutableArray2 = [.. immutableArray1];

        Assert.False(immutableArray1 == immutableArray2);
        Assert.True(immutableArray1.ToImmutableEquatableArray() == immutableArray2.ToImmutableEquatableArray());
    }

    [Fact]
    public void Empty_ShouldReturnEmptyArray()
    {
        var empty = ImmutableEquatableArray<string>.Empty;
        Assert.Equal(0, empty.Length);
    }

    [Fact]
    public void Constructor_ShouldCreateArrayWithCorrectLength()
    {
        var values = new[] { "a", "b", "c" };
        var array = ImmutableEquatableArray.Create(values);

        Assert.Equal(3, array.Length);
    }

    [Fact]
    public void Indexer_ShouldReturnCorrectValues()
    {
        var values = new[] { "a", "b", "c" };
        var array = ImmutableEquatableArray.Create(values);

        Assert.Equal("a", array[0]);
        Assert.Equal("b", array[1]);
        Assert.Equal("c", array[2]);
    }

    [Fact]
    public void Equals_SameReference_ShouldReturnTrue()
    {
        var array = ImmutableEquatableArray.Create(new[] { "a", "b" });

        Assert.True(array.Equals(array));
        Assert.True(array == array);
        Assert.False(array != array);
    }

    [Fact]
    public void Equals_SameValues_ShouldReturnTrue()
    {
        var array1 = ImmutableEquatableArray.Create(new[] { "a", "b" });
        var array2 = ImmutableEquatableArray.Create(new[] { "a", "b" });

        Assert.True(array1.Equals(array2));
        Assert.True(array1 == array2);
        Assert.False(array1 != array2);
    }

    [Fact]
    public void Equals_DifferentValues_ShouldReturnFalse()
    {
        var array1 = ImmutableEquatableArray.Create(new[] { "a", "b" });
        var array2 = ImmutableEquatableArray.Create(new[] { "a", "c" });

        Assert.False(array1.Equals(array2));
        Assert.False(array1 == array2);
        Assert.True(array1 != array2);
    }

    [Fact]
    public void Equals_DifferentLength_ShouldReturnFalse()
    {
        var array1 = ImmutableEquatableArray.Create(new[] { "a", "b" });
        var array2 = ImmutableEquatableArray.Create(new[] { "a" });

        Assert.False(array1.Equals(array2));
        Assert.False(array1 == array2);
        Assert.True(array1 != array2);
    }

    [Fact]
    public void Equals_WithNull_ShouldReturnFalse()
    {
        var array = ImmutableEquatableArray.Create(new[] { "a" });

        Assert.False(array.Equals(null));
        Assert.False(array == null);
        Assert.False(null == array);
        Assert.True(array != null);
        Assert.True(null != array);
    }

    [Fact]
    public void Equals_Object_ShouldWorkCorrectly()
    {
        var array1 = ImmutableEquatableArray.Create(new[] { "a", "b" });
        var array2 = ImmutableEquatableArray.Create(new[] { "a", "b" });
        object obj = array2;

        Assert.True(array1.Equals(obj));
        Assert.False(array1.Equals("not an array"));
    }

    [Fact]
    public void GetHashCode_ShouldReturnLength()
    {
        var array = ImmutableEquatableArray.Create(new[] { "a", "b", "c" });

        Assert.Equal(3, array.GetHashCode());
    }

    [Fact]
    public void GetEnumerator_ShouldEnumerateAllValues()
    {
        var values = new[] { "a", "b", "c" };
        var array = ImmutableEquatableArray.Create(values);
        var enumerator = array.GetEnumerator();
        var result = new List<string>();

        while (enumerator.MoveNext())
        {
            result.Add(enumerator.Current);
        }

        Assert.Equal(values, result);
    }

    [Fact]
    public void IEnumerable_GetEnumerator_ShouldWork()
    {
        var values = new[] { "a", "b", "c" };
        var array = ImmutableEquatableArray.Create(values);
        var result = new List<string>();

        foreach (var item in (IEnumerable<string>)array)
        {
            result.Add(item);
        }

        Assert.Equal(values, result);
    }

    [Fact]
    public void IEnumerable_NonGeneric_GetEnumerator_ShouldWork()
    {
        var values = new[] { "a", "b", "c" };
        var array = ImmutableEquatableArray.Create(values);
        var result = new List<string>();

        foreach (string item in (System.Collections.IEnumerable)array)
        {
            result.Add(item);
        }

        Assert.Equal(values, result);
    }

    [Fact]
    public void IReadOnlyList_Properties_ShouldWork()
    {
        var array = ImmutableEquatableArray.Create(new[] { "a", "b", "c" });
        var readOnlyList = (IReadOnlyList<string>)array;

        Assert.Equal(3, readOnlyList.Count);
        Assert.Equal("a", readOnlyList[0]);
        Assert.Equal("b", readOnlyList[1]);
        Assert.Equal("c", readOnlyList[2]);
    }

    [Fact]
    public void ICollection_Properties_ShouldBeReadOnly()
    {
        var array = ImmutableEquatableArray.Create(new[] { "a", "b" });
        var collection = (ICollection<string>)array;

        Assert.True(collection.IsReadOnly);
        Assert.Equal(2, collection.Count);
    }

    [Fact]
    public void IList_Properties_ShouldBeReadOnly()
    {
        var array = ImmutableEquatableArray.Create(new[] { "a", "b" });
        var list = (System.Collections.IList)array;

        Assert.True(list.IsReadOnly);
        Assert.True(list.IsFixedSize);
        Assert.Equal(2, list.Count);
        Assert.False(list.IsSynchronized);
        Assert.Same(array, list.SyncRoot);
    }

    [Fact]
    public void ICollection_Contains_ShouldWork()
    {
        var array = ImmutableEquatableArray.Create(new[] { "a", "b", "c" });
        var collection = (ICollection<string>)array;

        Assert.Contains("a", collection, StringComparer.Ordinal);
        Assert.Contains("b", collection, StringComparer.Ordinal);
        Assert.DoesNotContain("d", collection, StringComparer.Ordinal);
    }

    [Fact]
    public void IList_IndexOf_ShouldWork()
    {
        var array = ImmutableEquatableArray.Create(new[] { "a", "b", "c" });
        var list = (IList<string>)array;

        Assert.Equal(0, list.IndexOf("a", StringComparer.Ordinal));
        Assert.Equal(1, list.IndexOf("b", StringComparer.Ordinal));
        Assert.Equal(-1, list.IndexOf("d", StringComparer.Ordinal));
    }

    [Fact]
    public void ICollection_CopyTo_ShouldWork()
    {
        var array = ImmutableEquatableArray.Create(new[] { "a", "b", "c" });
        var collection = (ICollection<string>)array;
        var target = new string[5];

        collection.CopyTo(target, 1);

        Assert.Equal(new[] { null, "a", "b", "c", null }, target);
    }

    [Fact]
    public void MutatingOperations_ShouldThrowInvalidOperationException()
    {
        var array = ImmutableEquatableArray.Create(new[] { "a", "b" });
        var collection = (ICollection<string>)array;
        var list = (IList<string>)array;
        var nonGenericList = (System.Collections.IList)array;

        Assert.Throws<InvalidOperationException>(() => collection.Add("c"));
        Assert.Throws<InvalidOperationException>(() => collection.Remove("a"));
        Assert.Throws<InvalidOperationException>(() => collection.Clear());
        Assert.Throws<InvalidOperationException>(() => list.Insert(0, "c"));
        Assert.Throws<InvalidOperationException>(() => list.RemoveAt(0));
        Assert.Throws<InvalidOperationException>(() => list[0] = "changed");
        Assert.Throws<InvalidOperationException>(() => nonGenericList.Add("c"));
        Assert.Throws<InvalidOperationException>(() => nonGenericList.Clear());
        Assert.Throws<InvalidOperationException>(() => nonGenericList.Insert(0, "c"));
        Assert.Throws<InvalidOperationException>(() => nonGenericList.Remove("a"));
        Assert.Throws<InvalidOperationException>(() => nonGenericList.RemoveAt(0));
        Assert.Throws<InvalidOperationException>(() => nonGenericList[0] = "changed");
    }

    [Fact]
    public void ToImmutableEquatableArray_EmptyCollection_ShouldReturnEmpty()
    {
        var result = new List<string>().ToImmutableEquatableArray();

        Assert.Same(ImmutableEquatableArray<string>.Empty, result);
    }

    [Fact]
    public void ToImmutableEquatableArray_NonEmptyCollection_ShouldCreateArray()
    {
        var values = new[] { "a", "b", "c" };
        var result = values.ToImmutableEquatableArray();

        Assert.Equal(3, result.Length);
        Assert.Equal("a", result[0]);
        Assert.Equal("b", result[1]);
        Assert.Equal("c", result[2]);
    }

    [Fact]
    public void Create_EmptySpan_ShouldReturnEmpty()
    {
        var result = ImmutableEquatableArray.Create(ReadOnlySpan<string>.Empty);

        Assert.Same(ImmutableEquatableArray<string>.Empty, result);
    }

    [Fact]
    public void Create_NonEmptySpan_ShouldCreateArray()
    {
        var values = new[] { "a", "b", "c" };
        var result = ImmutableEquatableArray.Create(values.AsSpan());

        Assert.Equal(3, result.Length);
        Assert.Equal("a", result[0]);
        Assert.Equal("b", result[1]);
        Assert.Equal("c", result[2]);
    }

    [Fact]
    public void Create_EmptyImmutableArray_ShouldReturnEmpty()
    {
        var result = ImmutableEquatableArray.Create(ImmutableArray<string>.Empty);

        Assert.Same(ImmutableEquatableArray<string>.Empty, result);
    }

    [Fact]
    public void Create_NonEmptyImmutableArray_ShouldCreateArray()
    {
        var immutableArray = ImmutableArray.Create("a", "b", "c");
        var result = ImmutableEquatableArray.Create(immutableArray);

        Assert.Equal(3, result.Length);
        Assert.Equal("a", result[0]);
        Assert.Equal("b", result[1]);
        Assert.Equal("c", result[2]);
    }

    [Fact]
    public void CollectionBuilder_ShouldWork()
    {
        ImmutableEquatableArray<string> array = ["a", "b", "c"];

        Assert.Equal(3, array.Length);
        Assert.Equal("a", array[0]);
        Assert.Equal("b", array[1]);
        Assert.Equal("c", array[2]);
    }
}
