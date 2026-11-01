---
title: Dictionaries and tries
description: Associate several values with a key, search keys by prefix, suggest completions, and build key-value lists without checks.
---

## Several values per key

[MultiValueDictionary\<TKey, TValue\>](cref:Meziantou.Framework.Collections.MultiValueDictionary{TKey,TValue}) associates a list of values with each key:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/MultiValueDictionaryTests.cs;region=MultiValueDictionary_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var headers = new MultiValueDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
headers.Add("Accept", "text/html");
headers.Add("accept", "application/json");
headers.AddRange("Cache-Control", ["no-cache", "no-store"]);

Assert.Equal(["text/html", "application/json"], headers["ACCEPT"]);
Assert.True(headers.Contains("Cache-Control", "no-store"));

// Remove one value, or the key and all its values
Assert.True(headers.Remove("Cache-Control", "no-cache"));
Assert.True(headers.Remove("Accept"));
Assert.Equal(["Cache-Control"], headers.Keys);
```
<!-- endref -->

- [Add](cref:Meziantou.Framework.Collections.MultiValueDictionary{TKey,TValue}.Add) adds a value to the values of a key, and [AddRange](cref:Meziantou.Framework.Collections.MultiValueDictionary{TKey,TValue}.AddRange) adds several. A key exists as long as it has values:
  adding an empty range of values to a missing key does not add the key.
- The indexer returns the values of a key as an `IReadOnlyCollection<TValue>`. Use [TryGetValue](cref:Meziantou.Framework.Collections.MultiValueDictionary{TKey,TValue}.TryGetValue) when the key may be
  missing.
- `Remove(key, value)` removes one value, and `Remove(key)` removes the key and all its values.
- `Contains(key, value)`, [ContainsKey](cref:Meziantou.Framework.Collections.MultiValueDictionary{TKey,TValue}.ContainsKey) and [ContainsValue](cref:Meziantou.Framework.Collections.MultiValueDictionary{TKey,TValue}.ContainsValue) check the content.

## Prefix search

[Trie\<TValue\>](cref:Meziantou.Framework.Collections.Trie{TValue}) stores values by key, and finds all the keys that start with a prefix:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/TrieTests.cs;region=Trie_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var commands = new Trie<string>(ignoreCase: true);
commands.Add("status", "Show the working tree status");
commands.Add("stash", "Stash the changes");
commands.Add("commit", "Record changes");

Assert.True(commands.TryGetValue("COMMIT", out var description));
Assert.Equal("Record changes", description);

// Find all the keys that start with a prefix
var matches = commands.StartsWith("sta").Select(item => item.Key).Order(StringComparer.Ordinal);
Assert.Equal(["stash", "status"], matches);
```
<!-- endref -->

Pass `ignoreCase: true` to the constructor to compare keys without case. [StartsWith](cref:Meziantou.Framework.Collections.Trie{TValue}.StartsWith) returns the key-value pairs whose
key starts with the prefix. Enumerating the trie visits a key before the keys that extend it, and uses an explicit stack,
so it does not allocate per node.

## Ranked completions

[PruningRadixTree](cref:Meziantou.Framework.Collections.PruningRadixTree) stores terms with a frequency, and returns the most frequent terms that start with a prefix, for
search boxes and command palettes:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/PruningRadixTreeTests.cs;region=PruningRadixTree_GetTopTermsByPrefix;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = new PruningRadixTree();
tree.Add("car", 10);
tree.Add("cart", 5);
tree.Add("cat", 7);

var suggestions = tree.GetTopTermsByPrefix("ca", topK: 2, out _);
Assert.Equal([KeyValuePair.Create("car", 10L), KeyValuePair.Create("cat", 7L)], suggestions);
```
<!-- endref -->

- [GetTopTermsByPrefix](cref:Meziantou.Framework.Collections.PruningRadixTree.GetTopTermsByPrefix) returns at most `topK` terms, the most frequent first. Its `out` parameter receives the
  frequency of the prefix itself when it is also a term, and 0 otherwise.
- The tree skips the branches whose frequencies cannot reach the results, so the lookup stays fast with many terms.
  Pass `pruning: false` to visit every branch.
- Terms are case-sensitive: normalize them, for instance to lower case, before adding and searching.

## Lists of key-value pairs

[UnsafeListDictionary\<TKey, TValue\>](cref:Meziantou.Framework.Collections.UnsafeListDictionary{TKey,TValue}) is a dictionary backed by a list. [Add](cref:Meziantou.Framework.Collections.UnsafeListDictionary{TKey,TValue}.Add) does not check whether the key already
exists, which makes it cheap to build a list of pairs you know are unique, for instance to serialize them:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/UnsafeListDictionaryTests.cs;region=UnsafeListDictionary_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Adding does not check whether the key already exists
var values = new UnsafeListDictionary<string, int>(StringComparer.Ordinal);
values.Add("a", 1);
values.Add("b", 2);

Assert.Equal(2, values["b"]);
Assert.Equal(["a", "b"], values.Select(item => item.Key));
```
<!-- endref -->

The items keep the order in which you added them. Looking up a key scans the list, so prefer `Dictionary<TKey, TValue>`
for large collections that you query.

Pass an `IEqualityComparer<TKey>` to the constructor to change how keys are compared:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/UnsafeListDictionaryTests.cs;region=UnsafeListDictionary_Comparer;format=md-fenced-code-block;dedent=true -->
```csharp
var headers = new UnsafeListDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
headers.Add("Content-Type", "text/plain");

Assert.Equal("text/plain", headers["content-type"]);
```
<!-- endref -->

[Keys](cref:Meziantou.Framework.Collections.UnsafeListDictionary{TKey,TValue}.Keys) and [Values](cref:Meziantou.Framework.Collections.UnsafeListDictionary{TKey,TValue}.Values) return read-only views of
the dictionary: they do not copy the items, and they reflect later changes.
