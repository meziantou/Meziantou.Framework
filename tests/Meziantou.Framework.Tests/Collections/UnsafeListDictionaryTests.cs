using System.Text.Json;
using Meziantou.Framework.Collections;

namespace Meziantou.Framework.Tests.Collections;

public class UnsafeListDictionaryTests
{
    [Fact]
    public void TestDictionary()
    {
        UnsafeListDictionary<int, string> dict = new()
        {
            { 1, "a" },
            { 2, "b" },
            { 2, "c" },
        };

        Assert.HasCount(3, dict); // Allows duplicate values
        Assert.Contains(1, (IReadOnlyDictionary<int, string>)dict);
        Assert.Contains(2, (IReadOnlyDictionary<int, string>)dict);
        Assert.DoesNotContain(4, (IReadOnlyDictionary<int, string>)dict);

        dict[1] = "d";
        Assert.HasCount(3, dict); // Replace existing item
        Assert.Equal([1, 2, 2], dict.Keys);
        Assert.Equal(["d", "b", "c"], dict.Values);

        dict.Clear();
        Assert.Empty(dict);

        dict.AddRange(new KeyValuePair<int, string>[] { new(4, "a"), new(5, "e") });
        Assert.Equal([4, 5], dict.Keys);
    }

    [Fact]
    public void JsonSerializable()
    {
        UnsafeListDictionary<int, string> dict = new()
        {
            { 1, "a" },
            { 2, "b" },
            { 3, "c" },
        };

        var json = JsonSerializer.Serialize(dict);
        Assert.StartsWith(['{'], json);
        var deserialized = JsonSerializer.Deserialize<UnsafeListDictionary<int, string>>(json);
        Assert.Equal(dict, deserialized);
    }

    [Fact]
    public void Documentation_Usage()
    {
        #region UnsafeListDictionary_Usage
        // Adding does not check whether the key already exists
        var values = new UnsafeListDictionary<string, int>(StringComparer.Ordinal);
        values.Add("a", 1);
        values.Add("b", 2);

        Assert.Equal(2, values["b"]);
        Assert.Equal(["a", "b"], values.Select(item => item.Key));
        #endregion
    }

    [Fact]
    public void Documentation_Comparer()
    {
        #region UnsafeListDictionary_Comparer
        var headers = new UnsafeListDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        headers.Add("Content-Type", "text/plain");

        Assert.Equal("text/plain", headers["content-type"]);
        #endregion
    }

    [Fact]
    public void CustomComparer()
    {
        var dict = new UnsafeListDictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "a", 1 },
        };

        Assert.Same(StringComparer.OrdinalIgnoreCase, dict.Comparer);
        Assert.Contains("A", (IReadOnlyDictionary<string, int>)dict);
        Assert.Equal(1, dict["A"]);
        Assert.True(dict.TryGetValue("A", out var value));
        Assert.Equal(1, value);

        dict["A"] = 2;
        Assert.HasCount(1, dict);
        Assert.Equal(["a"], dict.Keys);
        Assert.Equal([2], dict.Values);

        Assert.True(dict.Remove("A"));
        Assert.Empty(dict);
    }

    [Fact]
    public void CustomComparer_Constructors()
    {
        var withCapacity = new UnsafeListDictionary<string, int>(4, StringComparer.OrdinalIgnoreCase);
        withCapacity.Add("a", 1);
        Assert.Contains("A", (IReadOnlyDictionary<string, int>)withCapacity);

        var withItems = new UnsafeListDictionary<string, int>([new KeyValuePair<string, int>("a", 1)], StringComparer.OrdinalIgnoreCase);
        Assert.Contains("A", (IReadOnlyDictionary<string, int>)withItems);

        var defaultComparer = new UnsafeListDictionary<int, int>(comparer: null);
        Assert.Same(EqualityComparer<int>.Default, defaultComparer.Comparer);
    }

    [Fact]
    public void KeysAndValues_AreLiveReadOnlyViews()
    {
        var dict = new UnsafeListDictionary<int, string>();
        var keys = dict.Keys;
        var values = dict.Values;

        dict.Add(1, "a");
        dict.Add(2, "b");

        Assert.Equal([1, 2], keys);
        Assert.Equal(["a", "b"], values);
        Assert.HasCount(2, keys);
        Assert.Contains(2, keys);
        Assert.Contains("b", values);
        Assert.DoesNotContain("c", values);

        var keyArray = new int[3];
        keys.CopyTo(keyArray, 1);
        Assert.Equal([0, 1, 2], keyArray);

        var valueArray = new string[2];
        values.CopyTo(valueArray, 0);
        Assert.Equal(["a", "b"], valueArray);

        ICollection<int> keyCollection = keys;
        Assert.True(keyCollection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => keyCollection.Add(3));
        ICollection<string> valueCollection = ((IDictionary<int, string>)dict).Values;
        Assert.Throws<NotSupportedException>(() => valueCollection.Remove("a"));
    }

    [Fact]
    public void Keys_EnumeratorIsStruct()
    {
        var dict = new UnsafeListDictionary<int, string> { { 1, "a" } };

        UnsafeListDictionary<int, string>.KeyCollection.Enumerator keys = dict.Keys.GetEnumerator();
        Assert.True(keys.MoveNext());
        Assert.Equal(1, keys.Current);
        Assert.False(keys.MoveNext());

        UnsafeListDictionary<int, string>.ValueCollection.Enumerator values = dict.Values.GetEnumerator();
        Assert.True(values.MoveNext());
        Assert.Equal("a", values.Current);
        Assert.False(values.MoveNext());
    }
}
