using Meziantou.Framework.Collections;

namespace Meziantou.Framework.Tests.Collections;

public sealed class PruningRadixTreeTests
{
    [Fact]
    public void AddAggregatesFrequency()
    {
        var tree = new PruningRadixTree();

        tree.Add("car", 10);
        tree.Add("car", 4);

        Assert.True(tree.TryGetValue("car", out var frequency));
        Assert.Equal(14, frequency);
        Assert.Equal(1, tree.Count);
    }

    [Fact]
    public void AddRangeBuildsTreeAndAggregatesDuplicates()
    {
        var tree = new PruningRadixTree();
        tree.AddRange(
        [
            KeyValuePair.Create("car", 10L),
            KeyValuePair.Create("cat", 5L),
            KeyValuePair.Create("car", 2L),
            KeyValuePair.Create("", 3L),
        ]);

        Assert.Equal(3, tree.Count);
        Assert.True(tree.TryGetValue("car", out var carFrequency));
        Assert.Equal(12, carFrequency);
        Assert.True(tree.TryGetValue("", out var emptyFrequency));
        Assert.Equal(3, emptyFrequency);
    }

    [Fact]
    public void AddRangeMergesWithExistingTerms()
    {
        var tree = new PruningRadixTree();
        tree.Add("car", 2);
        tree.Add("dog", 1);

        tree.AddRange(
        [
            KeyValuePair.Create("car", 3L),
            KeyValuePair.Create("cart", 5L),
            KeyValuePair.Create("dog", 4L),
        ]);

        Assert.Equal(3, tree.Count);
        Assert.True(tree.TryGetValue("car", out var carFrequency));
        Assert.Equal(5, carFrequency);
        Assert.True(tree.TryGetValue("dog", out var dogFrequency));
        Assert.Equal(5, dogFrequency);
        Assert.True(tree.TryGetValue("cart", out var cartFrequency));
        Assert.Equal(5, cartFrequency);
    }

    [Fact]
    public void AddRangeThrowsForInvalidFrequency()
    {
        var tree = new PruningRadixTree();

        Assert.Throws<ArgumentOutOfRangeException>(() => tree.AddRange([KeyValuePair.Create("car", 0L)]));
    }

    [Fact]
    public void AddRangeThrowsForNullKey()
    {
        var tree = new PruningRadixTree();

        Assert.Throws<ArgumentNullException>(() => tree.AddRange([new KeyValuePair<string, long>(null!, 1)]));
    }

    [Fact]
    public void GetTopTermsByPrefixReturnsRankedResults()
    {
        var tree = new PruningRadixTree();
        tree.Add("car", 10);
        tree.Add("cart", 12);
        tree.Add("cat", 12);
        tree.Add("dog", 100);

        var results = tree.GetTopTermsByPrefix("ca", topK: 3, out var prefixFrequency).ToArray();

        Assert.Equal(0, prefixFrequency);
        Assert.Equal(["cart", "cat", "car"], results.Select(result => result.Key).ToArray());
        Assert.Equal([12, 12, 10], results.Select(result => result.Value).ToArray());
    }

    [Fact]
    public void GetTopTermsByPrefixSetsPrefixFrequency()
    {
        var tree = new PruningRadixTree();
        tree.Add("ca", 2);
        tree.Add("car", 10);
        tree.Add("cat", 7);

        var results = tree.GetTopTermsByPrefix("ca", topK: 2, out var prefixFrequency);

        Assert.Equal(2, prefixFrequency);
        Assert.Equal(["car", "cat"], results.Select(result => result.Key).ToArray());
    }

    [Fact]
    public void GetTopTermsByPrefixInsideEdgeReturnsChildren()
    {
        var tree = new PruningRadixTree();
        tree.Add("cart", 5);
        tree.Add("carpet", 7);

        var results = tree.GetTopTermsByPrefix("ca", topK: 10, out _)
            .OrderByDescending(result => result.Value)
            .ThenBy(result => result.Key, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["carpet", "cart"], results.Select(result => result.Key).ToArray());
    }

    [Fact]
    public void PruningAndNonPruningReturnSameTopResults()
    {
        var tree = new PruningRadixTree();
        tree.Add("apple", 30);
        tree.Add("app", 40);
        tree.Add("application", 10);
        tree.Add("appetite", 25);
        tree.Add("apply", 35);
        tree.Add("apricot", 22);

        var withPruning = tree.GetTopTermsByPrefix("app", topK: 3, out var prefixFrequencyPruning, pruning: true).ToArray();
        var withoutPruning = tree.GetTopTermsByPrefix("app", topK: 3, out var prefixFrequencyNoPruning, pruning: false).ToArray();

        Assert.Equal(prefixFrequencyNoPruning, prefixFrequencyPruning);
        Assert.Equal(withoutPruning, withPruning);
    }

    [Fact]
    public void RemoveCompactsTreeAndPreservesOtherTerms()
    {
        var tree = new PruningRadixTree();
        tree.Add("test", 10);
        tree.Add("team", 8);
        tree.Add("te", 5);

        Assert.True(tree.Remove("team"));
        Assert.False(tree.TryGetValue("team", out _));
        Assert.True(tree.TryGetValue("test", out var testFrequency));
        Assert.Equal(10, testFrequency);
        Assert.True(tree.TryGetValue("te", out var teFrequency));
        Assert.Equal(5, teFrequency);
        Assert.Equal(2, tree.Count);
    }

    [Fact]
    public void CaseSensitiveStoresDistinctTerms()
    {
        var tree = new PruningRadixTree();
        tree.Add("Hello", 1);
        tree.Add("hello", 3);
        tree.Add("Heaven", 2);

        Assert.True(tree.TryGetValue("Hello", out var helloFrequency));
        Assert.Equal(1, helloFrequency);
        Assert.True(tree.TryGetValue("hello", out var lowerHelloFrequency));
        Assert.Equal(3, lowerHelloFrequency);
        Assert.False(tree.TryGetValue("HELLO", out _));

        var results = tree.GetTopTermsByPrefix("he", topK: 2, out _).ToArray();
        Assert.Equal(["hello"], results.Select(result => result.Key).ToArray());
        Assert.Equal([3], results.Select(result => result.Value).ToArray());
    }

    [Fact]
    public void SpanOverloadsWork()
    {
        var tree = new PruningRadixTree();
        tree.Add("car", 4);
        tree.Add("cart", 9);

        Assert.True(tree.TryGetValue("car".AsSpan(), out var carFrequency));
        Assert.Equal(4, carFrequency);
        Assert.True(tree.ContainsKey("cart".AsSpan()));
        Assert.True(tree.Remove("car".AsSpan()));
        Assert.False(tree.ContainsKey("car".AsSpan()));

        var results = tree.GetTopTermsByPrefix("ca".AsSpan(), topK: 5, out var prefixFrequency).ToArray();
        Assert.Equal(0, prefixFrequency);
        Assert.Equal(["cart"], results.Select(result => result.Key).ToArray());
    }
}
