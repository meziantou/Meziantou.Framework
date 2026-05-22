using Meziantou.Framework.Collections;

namespace Meziantou.Framework.Tests.Collections;

public sealed class TrieTests
{
    [Fact]
    public void AddAndTryGetValue()
    {
        var trie = new Trie<int>();

        trie.Add("car", 1);
        trie.Add("cat", 2);
        trie.Add("", 42);

        Assert.Equal(3, trie.Count);
        Assert.True(trie.TryGetValue("car", out var car));
        Assert.Equal(1, car);
        Assert.True(trie.TryGetValue("cat", out var cat));
        Assert.Equal(2, cat);
        Assert.True(trie.TryGetValue(string.Empty, out var rootValue));
        Assert.Equal(42, rootValue);
        Assert.False(trie.TryGetValue("dog", out _));
    }

    [Fact]
    public void AddDuplicateKeyThrows()
    {
        var trie = new Trie<int>();
        trie.Add("car", 1);

        Assert.Throws<ArgumentException>(() => trie.Add("car", 2));
    }

    [Fact]
    public void RemoveExistingAndMissingKeys()
    {
        var trie = new Trie<int>();
        trie.Add("car", 1);
        trie.Add("cart", 2);
        trie.Add("cat", 3);

        Assert.True(trie.Remove("cart"));
        Assert.Equal(2, trie.Count);
        Assert.False(trie.TryGetValue("cart", out _));

        Assert.False(trie.Remove("cart"));
        Assert.False(trie.Remove("dog"));
        Assert.Equal(2, trie.Count);
    }

    [Fact]
    public void StartsWithReturnsMatchingEntries()
    {
        var trie = new Trie<int>();
        trie.Add("car", 1);
        trie.Add("cart", 2);
        trie.Add("cat", 3);
        trie.Add("dog", 4);

        var entries = trie.StartsWith("ca").OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();

        Assert.Equal(["car", "cart", "cat"], entries.Select(entry => entry.Key).ToArray());
        Assert.Equal([1, 2, 3], entries.Select(entry => entry.Value).ToArray());
    }

    [Fact]
    public void StartsWithMissingPrefixReturnsEmpty()
    {
        var trie = new Trie<int>();
        trie.Add("car", 1);

        Assert.Empty(trie.StartsWith("zzz"));
    }

    [Fact]
    public void EnumerationReturnsAllEntries()
    {
        var trie = new Trie<int>();
        trie.Add("car", 1);
        trie.Add("dog", 2);
        trie.Add("", 3);

        var entries = trie.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();

        Assert.Equal(["", "car", "dog"], entries.Select(entry => entry.Key).ToArray());
        Assert.Equal([3, 1, 2], entries.Select(entry => entry.Value).ToArray());
    }

    [Fact]
    public void IgnoreCaseMatchesLookupRemoveAndPrefix()
    {
        var trie = new Trie<int>(ignoreCase: true);
        trie.Add("Hello", 1);
        trie.Add("heaven", 2);

        Assert.Throws<ArgumentException>(() => trie.Add("HELLO", 3));

        Assert.True(trie.TryGetValue("hello", out var hello));
        Assert.Equal(1, hello);
        Assert.True(trie.ContainsKey("HEAVEN"));

        var entries = trie.StartsWith("he").OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        Assert.Equal(["Hello", "heaven"], entries.Select(entry => entry.Key).ToArray());

        Assert.True(trie.Remove("hElLo"));
        Assert.False(trie.TryGetValue("HELLO", out _));
    }

    [Fact]
    public void CaseSensitiveDoesNotMatchDifferentCase()
    {
        var trie = new Trie<int>(ignoreCase: false);
        trie.Add("Hello", 1);

        Assert.False(trie.TryGetValue("hello", out _));
        Assert.Empty(trie.StartsWith("he"));
    }
}
