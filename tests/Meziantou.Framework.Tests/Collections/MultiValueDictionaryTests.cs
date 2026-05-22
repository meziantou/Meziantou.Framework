using Meziantou.Framework.Collections;

namespace Meziantou.Framework.Tests.Collections;

public sealed class MultiValueDictionaryTests
{
    private static MultiValueDictionary<string, int> CreateDictionary()
    {
        return new(StringComparer.Ordinal);
    }

    [Fact]
    public void Add_AllowsMultipleValuesForSameKey()
    {
        var dictionary = CreateDictionary();

        dictionary.Add("a", 1);
        dictionary.Add("a", 2);

        Assert.True(dictionary.ContainsKey("a"));
        Assert.Single(dictionary);
        Assert.Equal([1, 2], dictionary["a"]);
    }

    [Fact]
    public void AddRange_AddsValuesForNewAndExistingKeys()
    {
        var dictionary = CreateDictionary();

        dictionary.AddRange("a", [1, 2]);
        dictionary.AddRange("a", [3, 4]);
        dictionary.AddRange("b", [5]);

        Assert.Equal(2, dictionary.Count);
        Assert.Equal([1, 2, 3, 4], dictionary["a"]);
        Assert.Equal([5], dictionary["b"]);
    }

    [Fact]
    public void ReturnedCollection_ReflectsSubsequentChanges()
    {
        var dictionary = CreateDictionary();
        dictionary.Add("a", 1);
        var values = dictionary["a"];

        dictionary.Add("a", 2);

        Assert.Equal([1, 2], values);
    }

    [Fact]
    public void Remove_KeyValue_RemovesOnlyOneValue()
    {
        var dictionary = CreateDictionary();
        dictionary.Add("a", 1);
        dictionary.Add("a", 2);
        dictionary.Add("a", 2);

        Assert.True(dictionary.Remove("a", 2));
        Assert.Equal([1, 2], dictionary["a"]);
    }

    [Fact]
    public void Remove_LastValue_RemovesTheKey()
    {
        var dictionary = CreateDictionary();
        dictionary.Add("a", 1);

        Assert.True(dictionary.Remove("a", 1));
        Assert.False(dictionary.ContainsKey("a"));
        Assert.Empty(dictionary);
    }

    [Fact]
    public void Remove_Key_RemovesAllValues()
    {
        var dictionary = CreateDictionary();
        dictionary.AddRange("a", [1, 2, 3]);

        Assert.True(dictionary.Remove("a"));
        Assert.False(dictionary.ContainsKey("a"));
        Assert.Empty(dictionary);
    }

    [Fact]
    public void ContainsAndContainsValue_ReturnExpectedValues()
    {
        var dictionary = CreateDictionary();
        dictionary.AddRange("a", [1, 2]);
        dictionary.AddRange("b", [3, 4]);

        Assert.True(dictionary.Contains("a", 1));
        Assert.False(dictionary.Contains("a", 3));
        Assert.True(dictionary.ContainsValue(4));
        Assert.False(dictionary.ContainsValue(42));
    }

    [Fact]
    public void TryGetValue_ReturnsCollectionWhenFound()
    {
        var dictionary = CreateDictionary();
        dictionary.AddRange("a", [1, 2]);

        Assert.True(dictionary.TryGetValue("a", out var values));
        Assert.Equal([1, 2], values);

        Assert.False(dictionary.TryGetValue("missing", out var missingValues));
        Assert.Null(missingValues);
    }

    [Fact]
    public void Indexer_ThrowsWhenKeyIsMissing()
    {
        var dictionary = CreateDictionary();

        Assert.Throws<KeyNotFoundException>(() => _ = dictionary["missing"]);
    }

    [Fact]
    public void ConstructorFromEnumerable_CopiesValues()
    {
        var sourceValues = new List<int> { 1, 2 };
        IEnumerable<KeyValuePair<string, IReadOnlyCollection<int>>> source =
        [
            new("a", sourceValues),
            new("b", [3]),
        ];

        var dictionary = new MultiValueDictionary<string, int>(source, StringComparer.Ordinal);
        sourceValues.Add(99);

        Assert.Equal(2, dictionary.Count);
        Assert.Equal([1, 2], dictionary["a"]);
        Assert.Equal([3], dictionary["b"]);
    }

    [Fact]
    public void Constructor_UsesProvidedComparer()
    {
        var dictionary = new MultiValueDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        dictionary.Add("key", 1);
        dictionary.Add("KEY", 2);

        Assert.Single(dictionary);
        Assert.Equal([1, 2], dictionary["KeY"]);
    }

    [Fact]
    public void Enumeration_ReturnsKeysAndCollections()
    {
        var dictionary = CreateDictionary();
        dictionary.AddRange("a", [1, 2]);
        dictionary.AddRange("b", [3]);

        var pairs = dictionary.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);

        Assert.Equal([1, 2], pairs["a"]);
        Assert.Equal([3], pairs["b"]);
    }

    [Fact]
    public void AddRange_NullValues_Throws()
    {
        var dictionary = CreateDictionary();

        Assert.Throws<ArgumentNullException>(() => dictionary.AddRange("a", values: null!));
    }

    [Fact]
    public void Constructor_NullEnumerable_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MultiValueDictionary<string, int>(enumerable: null!, comparer: StringComparer.Ordinal));
    }
}
