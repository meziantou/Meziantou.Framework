#pragma warning disable CS1718 // Comparison made to same variable
#pragma warning disable xUnit2013 // Do not use equality check to check for collection size.
using Meziantou.Framework.Collections;
using Xunit;

namespace Meziantou.Framework.Tests.Collections;

public sealed class ImmutableEquatableDictionaryTests
{
    [Fact]
    public void Empty_ShouldReturnEmptyDictionary()
    {
        var empty = ImmutableEquatableDictionary<string, int>.Empty;

        Assert.Equal(0, empty.Count);
        Assert.Empty(empty);
        Assert.False(empty.ContainsKey("test"));
    }

    [Fact]
    public void Empty_ShouldReturnSameInstance()
    {
        var empty1 = ImmutableEquatableDictionary<string, int>.Empty;
        var empty2 = ImmutableEquatableDictionary<string, int>.Empty;

        Assert.Same(empty1, empty2);
    }

    [Fact]
    public void StaticEmpty_ShouldReturnEmptyDictionary()
    {
        var empty = ImmutableEquatableDictionary.Empty<string, int>();

        Assert.Equal(0, empty.Count);
        Assert.Empty(empty);
    }

    [Fact]
    public void ToImmutableEquatableDictionary_FromKeyValuePairs_ShouldCreateDictionary()
    {
        var pairs = new[]
        {
            new KeyValuePair<string, int>("a", 1),
            new KeyValuePair<string, int>("b", 2),
            new KeyValuePair<string, int>("c", 3),
        };

        var dict = pairs.ToImmutableEquatableDictionary();

        Assert.Equal(3, dict.Count);
        Assert.Equal(1, dict["a"]);
        Assert.Equal(2, dict["b"]);
        Assert.Equal(3, dict["c"]);
    }

    [Fact]
    public void ToImmutableEquatableDictionary_FromEmptyCollection_ShouldReturnEmpty()
    {
        var empty = Array.Empty<KeyValuePair<string, int>>().ToImmutableEquatableDictionary();

        Assert.Same(ImmutableEquatableDictionary<string, int>.Empty, empty);
    }

    [Fact]
    public void ToImmutableEquatableDictionary_WithKeySelector_ShouldCreateDictionary()
    {
        var values = new[] { "apple", "banana", "cherry" };

        var dict = values.ToImmutableEquatableDictionary(s => s[0]);

        Assert.Equal(3, dict.Count);
        Assert.Equal("apple", dict['a']);
        Assert.Equal("banana", dict['b']);
        Assert.Equal("cherry", dict['c']);
    }

    [Fact]
    public void ToImmutableEquatableDictionary_WithKeyAndValueSelectors_ShouldCreateDictionary()
    {
        var values = new[] { "apple", "banana", "cherry" };

        var dict = values.ToImmutableEquatableDictionary(s => s[0], s => s.Length);

        Assert.Equal(3, dict.Count);
        Assert.Equal(5, dict['a']); // "apple".Length
        Assert.Equal(6, dict['b']); // "banana".Length
        Assert.Equal(6, dict['c']); // "cherry".Length
    }

    [Fact]
    public void ContainsKey_ShouldReturnCorrectValue()
    {
        var dict = new[] { ("a", 1), ("b", 2) }.ToImmutableEquatableDictionary();

        Assert.True(dict.ContainsKey("a"));
        Assert.True(dict.ContainsKey("b"));
        Assert.False(dict.ContainsKey("c"));
    }

    [Fact]
    public void TryGetValue_ShouldReturnCorrectValue()
    {
        var dict = new[] { ("a", 1), ("b", 2) }.ToImmutableEquatableDictionary();

        Assert.True(dict.TryGetValue("a", out var value1));
        Assert.Equal(1, value1);

        Assert.True(dict.TryGetValue("b", out var value2));
        Assert.Equal(2, value2);

        Assert.False(dict.TryGetValue("c", out var value3));
        Assert.Equal(0, value3);
    }

    [Fact]
    public void Indexer_ShouldReturnCorrectValue()
    {
        var dict = new[] { ("a", 1), ("b", 2) }.ToImmutableEquatableDictionary();

        Assert.Equal(1, dict["a"]);
        Assert.Equal(2, dict["b"]);
    }

    [Fact]
    public void Indexer_WithMissingKey_ShouldThrow()
    {
        var dict = new[] { ("a", 1) }.ToImmutableEquatableDictionary();

        Assert.Throws<KeyNotFoundException>(() => dict["missing"]);
    }

    [Fact]
    public void Equals_SameReference_ShouldReturnTrue()
    {
        var dict = new[] { ("a", 1) }.ToImmutableEquatableDictionary();

        Assert.True(dict.Equals(dict));
        Assert.True(dict == dict);
        Assert.False(dict != dict);
    }

    [Fact]
    public void Equals_SameContent_ShouldReturnTrue()
    {
        var dict1 = new[] { ("a", 1), ("b", 2) }.ToImmutableEquatableDictionary();
        var dict2 = new[] { ("b", 2), ("a", 1) }.ToImmutableEquatableDictionary();

        Assert.True(dict1.Equals(dict2));
        Assert.True(dict1 == dict2);
        Assert.False(dict1 != dict2);
        Assert.True(dict1.Equals((object)dict2));
    }

    [Fact]
    public void Equals_DifferentContent_ShouldReturnFalse()
    {
        var dict1 = new[] { ("a", 1), ("b", 2) }.ToImmutableEquatableDictionary();
        var dict2 = new[] { ("a", 1), ("b", 3) }.ToImmutableEquatableDictionary();

        Assert.False(dict1.Equals(dict2));
        Assert.False(dict1 == dict2);
        Assert.True(dict1 != dict2);
        Assert.False(dict1.Equals((object)dict2));
    }

    [Fact]
    public void Equals_DifferentCounts_ShouldReturnFalse()
    {
        var dict1 = new[] { ("a", 1) }.ToImmutableEquatableDictionary();
        var dict2 = new[] { ("a", 1), ("b", 2) }.ToImmutableEquatableDictionary();

        Assert.False(dict1.Equals(dict2));
        Assert.False(dict1 == dict2);
        Assert.True(dict1 != dict2);
    }

    [Fact]
    public void Equals_WithNull_ShouldReturnFalse()
    {
        var dict = new[] { ("a", 1) }.ToImmutableEquatableDictionary();

        Assert.False(dict.Equals(null));
        Assert.False(dict == null);
        Assert.False(null == dict);
        Assert.True(dict != null);
        Assert.True(null != dict);
        Assert.False(dict.Equals((object?)null));
    }

    [Fact]
    public void Equals_BothNull_ShouldReturnTrue()
    {
        ImmutableEquatableDictionary<string, int>? dict1 = null;
        ImmutableEquatableDictionary<string, int>? dict2 = null;

        Assert.True(dict1 == dict2);
        Assert.False(dict1 != dict2);
    }

    [Fact]
    public void GetHashCode_SameContent_ShouldReturnSameValue()
    {
        var dict1 = new[] { ("a", 1), ("b", 2) }.ToImmutableEquatableDictionary();
        var dict2 = new[] { ("b", 2), ("a", 1) }.ToImmutableEquatableDictionary();

        Assert.Equal(dict1.GetHashCode(), dict2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentContent_MayReturnDifferentValues()
    {
        var dict1 = new[] { ("a", 1) }.ToImmutableEquatableDictionary();
        var dict2 = new[] { ("a", 1), ("b", 2) }.ToImmutableEquatableDictionary();

        Assert.NotEqual(dict1.GetHashCode(), dict2.GetHashCode());
    }

    [Fact]
    public void Enumeration_ShouldReturnAllKeyValuePairs()
    {
        var expected = new[] { ("a", 1), ("b", 2), ("c", 3) };
        var dict = expected.ToImmutableEquatableDictionary();

        var actual = new List<(string, int)>();
        foreach (var kvp in dict)
        {
            actual.Add((kvp.Key, kvp.Value));
        }

        Assert.Equal(expected.Length, actual.Count);
        foreach (var item in expected)
        {
            Assert.Contains(item, actual);
        }
    }

    [Fact]
    public void Keys_ShouldReturnAllKeys()
    {
        var dict = new[] { ("a", 1), ("b", 2), ("c", 3) }.ToImmutableEquatableDictionary();

        var keys = dict.Keys;

        Assert.Equal(3, keys.Count);
        Assert.Contains("a", keys);
        Assert.Contains("b", keys);
        Assert.Contains("c", keys);
    }

    [Fact]
    public void Values_ShouldReturnAllValues()
    {
        var dict = new[] { ("a", 1), ("b", 2), ("c", 3) }.ToImmutableEquatableDictionary();

        var values = dict.Values;

        Assert.Equal(3, values.Count);
        Assert.Contains(1, values);
        Assert.Contains(2, values);
        Assert.Contains(3, values);
    }

    [Fact]
    public void IDictionary_MutatingOperations_ShouldThrow()
    {
        var dict = new[] { ("a", 1) }.ToImmutableEquatableDictionary();
        var idict = (IDictionary<string, int>)dict;

        Assert.Throws<InvalidOperationException>(() => idict.Add("b", 2));
        Assert.Throws<InvalidOperationException>(() => idict.Remove("a"));
        Assert.Throws<InvalidOperationException>(() => idict["a"] = 2);
    }

    [Fact]
    public void ICollection_MutatingOperations_ShouldThrow()
    {
        var dict = new[] { ("a", 1) }.ToImmutableEquatableDictionary();
        var icoll = (ICollection<KeyValuePair<string, int>>)dict;

        Assert.Throws<InvalidOperationException>(() => icoll.Add(new KeyValuePair<string, int>("b", 2)));
        Assert.Throws<InvalidOperationException>(() => icoll.Remove(new KeyValuePair<string, int>("a", 1)));
        Assert.Throws<InvalidOperationException>(() => icoll.Clear());
    }

    [Fact]
    public void IDictionary_NonGeneric_MutatingOperations_ShouldThrow()
    {
        var dict = new[] { ("a", 1) }.ToImmutableEquatableDictionary();
        var idict = (System.Collections.IDictionary)dict;

        Assert.Throws<InvalidOperationException>(() => idict.Add("b", 2));
        Assert.Throws<InvalidOperationException>(() => idict.Remove("a"));
        Assert.Throws<InvalidOperationException>(() => idict.Clear());
        Assert.Throws<InvalidOperationException>(() => idict["a"] = 2);
    }

    [Fact]
    public void ICollection_Properties_ShouldReturnCorrectValues()
    {
        var dict = new[] { ("a", 1) }.ToImmutableEquatableDictionary();
        var icoll = (ICollection<KeyValuePair<string, int>>)dict;

        Assert.True(icoll.IsReadOnly);
        Assert.True(icoll.Contains(new KeyValuePair<string, int>("a", 1)));
        Assert.False(icoll.Contains(new KeyValuePair<string, int>("a", 2)));
    }

    [Fact]
    public void IDictionary_NonGeneric_Properties_ShouldReturnCorrectValues()
    {
        var dict = new[] { ("a", 1) }.ToImmutableEquatableDictionary();
        var idict = (System.Collections.IDictionary)dict;

        Assert.True(idict.IsReadOnly);
        Assert.True(idict.IsFixedSize);
        Assert.False(idict.IsSynchronized);
        Assert.Same(dict, idict.SyncRoot);
        Assert.True(idict.Contains("a"));
        Assert.False(idict.Contains("b"));
        Assert.Equal(1, idict["a"]);
    }

    [Fact]
    public void CopyTo_ShouldCopyAllElements()
    {
        var dict = new[] { ("a", 1), ("b", 2) }.ToImmutableEquatableDictionary();
        var icoll = (ICollection<KeyValuePair<string, int>>)dict;
        var array = new KeyValuePair<string, int>[3];

        icoll.CopyTo(array, 1);

        Assert.Equal(default, array[0]);
        Assert.Contains(new KeyValuePair<string, int>("a", 1), array);
        Assert.Contains(new KeyValuePair<string, int>("b", 2), array);
    }

    [Fact]
    public void ToImmutableEquatableDictionary_FromDictionary_ShouldWork()
    {
        var source = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = 2 };
        var dict = source.ToImmutableEquatableDictionary();

        Assert.Equal(2, dict.Count);
        Assert.Equal(1, dict["a"]);
        Assert.Equal(2, dict["b"]);
    }
}