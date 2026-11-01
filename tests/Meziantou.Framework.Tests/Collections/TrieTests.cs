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

    [Fact]
    public void Documentation_Usage()
    {
        #region Trie_Usage
        var commands = new Trie<string>(ignoreCase: true);
        commands.Add("status", "Show the working tree status");
        commands.Add("stash", "Stash the changes");
        commands.Add("commit", "Record changes");

        Assert.True(commands.TryGetValue("COMMIT", out var description));
        Assert.Equal("Record changes", description);

        // Find all the keys that start with a prefix
        var matches = commands.StartsWith("sta").Select(item => item.Key).Order(StringComparer.Ordinal);
        Assert.Equal(["stash", "status"], matches);
        #endregion
    }

    [Fact]
    public void Enumerator_IsStruct_EnumeratesDeepKeys()
    {
        var trie = new Trie<int>();
        var expected = new List<KeyValuePair<string, int>>();
        for (var i = 0; i <= 200; i++)
        {
            var key = new string('a', i);
            trie.Add(key, i);
            expected.Add(new(key, i));
        }

        trie.Add("b", -1);
        trie.Add("ab", -2);

        Trie<int>.Enumerator enumerator = trie.GetEnumerator();
        var actual = new List<KeyValuePair<string, int>>();
        while (enumerator.MoveNext())
        {
            actual.Add(enumerator.Current);
        }

        Assert.HasCount(trie.Count, actual);
        Assert.Equal(expected.Concat([new KeyValuePair<string, int>("b", -1), new KeyValuePair<string, int>("ab", -2)]).OrderBy(item => item.Key, StringComparer.Ordinal), actual.OrderBy(item => item.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void Enumerate_ParentBeforeChildren()
    {
        var trie = new Trie<int>();
        trie.Add("abc", 3);
        trie.Add("a", 1);
        trie.Add("ab", 2);
        trie.Add("x", 4);

        var keys = trie.Select(item => item.Key).ToList();
        Assert.True(keys.IndexOf("a") < keys.IndexOf("ab"));
        Assert.True(keys.IndexOf("ab") < keys.IndexOf("abc"));
        Assert.HasCount(4, keys);
    }

    [Fact]
    public void Enumerate_Empty()
    {
        var trie = new Trie<int>();
        Assert.Empty(trie);
        Assert.Empty(trie.StartsWith("a"));
    }

    [Fact]
    public void Enumerator_Reset()
    {
        var trie = new Trie<int>();
        trie.Add("a", 1);
        trie.Add("ab", 2);

        IEnumerator<KeyValuePair<string, int>> enumerator = trie.GetEnumerator();
        while (enumerator.MoveNext())
        {
        }

        enumerator.Reset();
        var count = 0;
        while (enumerator.MoveNext())
        {
            count++;
        }

        Assert.Equal(2, count);
    }
}
