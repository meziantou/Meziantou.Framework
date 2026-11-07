---
title: Collections
description: Check the items, the count, the single item, every item, duplicates and sets of a collection or async sequence.
---

The assertions of this page accept any `IEnumerable<T>`, and most of them also accept a `ReadOnlySpan<T>` or an
`IAsyncEnumerable<T>`. A `null` collection fails them, except where noted.

## Items and subsequences

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertContainsTests.cs;region=Assert_CollectionItems;format=md-fenced-code-block;dedent=true -->
```csharp
List<int> numbers = [1, 2, 3, 4];

Assert.Contains(3, numbers);                    // an item
Assert.DoesNotContain(5, numbers);
Assert.Contains(new[] { 2, 3 }, numbers);       // a subsequence: consecutive items, in order
Assert.StartsWith(new[] { 1, 2 }, numbers);
Assert.EndsWith(4, numbers);

Assert.Contains(numbers, n => n > 3);           // an item that matches a predicate
Assert.DoesNotContain(numbers, n => n > 10);
```
<!-- endref -->

- `Contains(item, collection)` looks for an item. Without a comparer, an `ICollection<T>`, such as a `HashSet<T>`
  created with a case-insensitive comparer, decides with its own [Contains](cref:Meziantou.Framework.Assertions.Assert.Contains), like `Enumerable.Contains`. Pass an
  `IEqualityComparer<T>` to choose how items are compared.
- When the expected value is a sequence of the item type, such as an `int[]` in a `List<int>`, it is a
  **subsequence**: the collection must contain these items, consecutive and in order. [StartsWith](cref:Meziantou.Framework.Assertions.Assert.StartsWith) and [EndsWith](cref:Meziantou.Framework.Assertions.Assert.EndsWith)
  check a prefix or a suffix the same way, and also accept a single item.
- `Contains(collection, predicate)` looks for an item that matches a predicate. The collection comes first, as there is
  no expected value.
- When [DoesNotContain](cref:Meziantou.Framework.Assertions.Assert.DoesNotContain) fails, the message shows the index where the item was found.

## Dictionaries

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertContainsTests.cs;region=Assert_DictionaryKeys;format=md-fenced-code-block;dedent=true -->
```csharp
var ages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
{
    ["Alice"] = 42,
};

// The dictionary looks the key up with its own comparer, and the assertion returns the value
var age = Assert.Contains("ALICE", ages);
Assert.Equal(42, age);
Assert.DoesNotContain("Bob", ages);
```
<!-- endref -->

`Contains(key, dictionary)` returns the value of the key, so the test can check it. Without an explicit comparer, the
dictionary decides, with its own comparer, whether it contains the key.

## Counts and emptiness

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertCountTests.cs;region=Assert_Counts;format=md-fenced-code-block;dedent=true -->
```csharp
List<string> empty = [];
List<int> numbers = [1, 2, 3, 4];

Assert.Empty(empty);
Assert.NotEmpty(numbers);
Assert.HasCount(4, numbers);                    // the count comes first
Assert.DoesNotHaveCount(3, numbers);
Assert.HasCountGreaterThan(2, numbers);         // numbers has more than 2 items
Assert.HasCountLessThanOrEqual(4, numbers);
```
<!-- endref -->

| Assertion | Passes when the count is |
| --- | --- |
| `HasCount(n, collection)` | `n` |
| `DoesNotHaveCount(n, collection)` | anything but `n` |
| `HasCountGreaterThan(n, collection)` | more than `n` |
| `HasCountGreaterThanOrEqual(n, collection)` | `n` or more |
| `HasCountLessThan(n, collection)` | less than `n` |
| `HasCountLessThanOrEqual(n, collection)` | `n` or less |
| `Empty(collection)` | 0 |
| `NotEmpty(collection)` | 1 or more |

Prefer these assertions to `Assert.Equal(3, list.Count)`: their failure message shows the items of the collection, not
only two numbers. The analyzer suggests them (`MFAS0004`, `MFAS0005`, `MFAS0030`).

## Lazy sequences

The count assertions read a lazy sequence only as far as the comparison requires, and a subsequence search stops at the
first match, so they work on endless sequences:

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertCountTests.cs;region=Assert_LazySequences;format=md-fenced-code-block;dedent=true -->
```csharp
// An endless sequence: the assertions read only the items they need
static IEnumerable<int> Naturals()
{
    for (var i = 0; ; i++)
        yield return i;
}

Assert.HasCountGreaterThan(3, Naturals());      // reads 4 items
Assert.Contains(new[] { 5, 6 }, Naturals());    // stops at the first match
```
<!-- endref -->

When a sequence is longer than the count the assertion needed to read, the failure message reports its count as a
lower bound (`at least N`). [All](cref:Meziantou.Framework.Assertions.Assert.All) reads a sequence once, and [Collection](cref:Meziantou.Framework.Assertions.Assert.Collection) reads at most one item past its inspectors.

## The single item

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertSingleTests.cs;region=Assert_Single;format=md-fenced-code-block;dedent=true -->
```csharp
List<string> users = ["Alice", "Bob", "Charlie"];

var user = Assert.Single(users, name => name.Length == 3);      // the only item that matches
Assert.Equal("Bob", user);

var admin = Assert.Single(new[] { "root" });                    // the only item
Assert.Equal("root", admin);
```
<!-- endref -->

[Single](cref:Meziantou.Framework.Assertions.Assert.Single) fails when the collection is empty or has more than one item, or, with a predicate, when no item or more than
one item matches. It returns the item.

## Every item

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertAllTests.cs;region=Assert_All;format=md-fenced-code-block;dedent=true -->
```csharp
List<int> numbers = [2, 4, 6];

Assert.All(numbers, n => n % 2 == 0);                         // a predicate
Assert.All(numbers, n => Assert.InRange(n, 1, 10));           // an assertion
Assert.All(numbers, (n, index) => Assert.Equal((index + 1) * 2, n));
Assert.DoesNotAll(numbers, n => n > 5);                       // at least one item does not match
```
<!-- endref -->

- `All(collection, predicate)` checks that every item matches a predicate.
- `All(collection, assertion)` runs an assertion on every item, optionally with the index of the item. The failure
  message shows the index of the first item that failed, and the failure of its assertion.
- `DoesNotAll(collection, predicate)` checks that at least one item does not match, like
  `!collection.All(predicate)`: an empty collection fails it.

## Each item in order

[Collection](cref:Meziantou.Framework.Assertions.Assert.Collection) runs one inspector per item, in order. It fails when the collection has more or fewer items than
inspectors:

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertCollectionTests.cs;region=Assert_Collection;format=md-fenced-code-block;dedent=true -->
```csharp
List<string> users = ["Alice", "Bob"];

// One inspector per item, in order: the collection must have exactly as many items
Assert.Collection(users,
    user => Assert.Equal("Alice", user),
    user => Assert.StartsWith("B", user));
```
<!-- endref -->

## Duplicates

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertDistinctTests.cs;region=Assert_Distinct;format=md-fenced-code-block;dedent=true -->
```csharp
string[] ids = ["a", "b", "c"];
string[] tags = ["dotnet", "DotNet"];

Assert.Distinct(ids);
Assert.Distinct(tags);
Assert.NotDistinct(tags, StringComparer.OrdinalIgnoreCase);
```
<!-- endref -->

[Distinct](cref:Meziantou.Framework.Assertions.Assert.Distinct) checks that no item appears twice, and [NotDistinct](cref:Meziantou.Framework.Assertions.Assert.NotDistinct) that at least one does. Both accept an
`IEqualityComparer<T>`.

## Sets

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertSetTests.cs;region=Assert_Sets;format=md-fenced-code-block;dedent=true -->
```csharp
var allowed = new HashSet<string>(StringComparer.Ordinal) { "read", "write", "delete" };
string[] granted = ["read", "write"];

Assert.Subset(allowed, granted);                // every item of granted is in allowed
Assert.ProperSubset(allowed, granted);          // ...and allowed has more items
Assert.Superset(granted, allowed);              // every item of granted is in allowed
Assert.NotProperSuperset(allowed, granted);
```
<!-- endref -->

| Assertion | Passes when `actual` is |
| --- | --- |
| `Subset(expectedSuperset, actual)` | a subset of `expectedSuperset`, like `actual.IsSubsetOf(expectedSuperset)` |
| `ProperSubset(expectedSuperset, actual)` | a subset, and `expectedSuperset` has an item that `actual` does not |
| `NotProperSubset(expectedSuperset, actual)` | not a proper subset |
| `Superset(expectedSubset, actual)` | a superset of `expectedSubset`, like `actual.IsSupersetOf(expectedSubset)` |
| `ProperSuperset(expectedSubset, actual)` | a superset, and `actual` has an item that `expectedSubset` does not |
| `NotProperSuperset(expectedSubset, actual)` | not a proper superset |

A `null` `actual` fails [Subset](cref:Meziantou.Framework.Assertions.Assert.Subset), [ProperSubset](cref:Meziantou.Framework.Assertions.Assert.ProperSubset), [Superset](cref:Meziantou.Framework.Assertions.Assert.Superset) and [ProperSuperset](cref:Meziantou.Framework.Assertions.Assert.ProperSuperset), and passes [NotProperSubset](cref:Meziantou.Framework.Assertions.Assert.NotProperSubset) and
[NotProperSuperset](cref:Meziantou.Framework.Assertions.Assert.NotProperSuperset). Without a comparer, a set passed as `actual`, such as a `HashSet<T>`, compares the items with its
own comparer.

## Async sequences

The assertions on an `IAsyncEnumerable<T>` read it asynchronously and return a `Task`: await them. An assertion that is
not awaited never runs, and the analyzer reports it as an error (`MFAS0048`).

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertEqualUnorderedTests.cs;region=Assert_AsyncSequences;format=md-fenced-code-block;dedent=true -->
```csharp
static async IAsyncEnumerable<int> GetValuesAsync()
{
    for (var i = 1; i <= 3; i++)
    {
        await Task.Yield();
        yield return i;
    }
}

// Assertions on an IAsyncEnumerable<T> return a Task: await them
await Assert.Equal(new[] { 1, 2, 3 }, GetValuesAsync());
await Assert.HasCount(3, GetValuesAsync());
await Assert.Contains(new[] { 2, 3 }, GetValuesAsync());
```
<!-- endref -->

[Equal](cref:Meziantou.Framework.Assertions.Assert.Equal), [NotEqual](cref:Meziantou.Framework.Assertions.Assert.NotEqual), [EqualUnordered](cref:Meziantou.Framework.Assertions.Assert.EqualUnordered), [Empty](cref:Meziantou.Framework.Assertions.Assert.Empty), [NotEmpty](cref:Meziantou.Framework.Assertions.Assert.NotEmpty), [Single](cref:Meziantou.Framework.Assertions.Assert.Single), [All](cref:Meziantou.Framework.Assertions.Assert.All), [Distinct](cref:Meziantou.Framework.Assertions.Assert.Distinct), [NotDistinct](cref:Meziantou.Framework.Assertions.Assert.NotDistinct), the count
assertions, and the subsequence forms of [Contains](cref:Meziantou.Framework.Assertions.Assert.Contains), [DoesNotContain](cref:Meziantou.Framework.Assertions.Assert.DoesNotContain), [StartsWith](cref:Meziantou.Framework.Assertions.Assert.StartsWith) and [EndsWith](cref:Meziantou.Framework.Assertions.Assert.EndsWith) accept an
`IAsyncEnumerable<T>`. [Equal](cref:Meziantou.Framework.Assertions.Assert.Equal) compares it with another async sequence or with a regular collection.
