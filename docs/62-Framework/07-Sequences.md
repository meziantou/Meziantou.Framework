---
title: Sequences
description: Filter null values, check sequences, loop asynchronously, cache enumerations, and update collections and dictionaries.
---

## Filter null and empty values

<!-- ref:../../tests/Meziantou.Framework.Tests/EnumerableTests.cs;region=EnumerableExtensions_Filtering;format=md-fenced-code-block;dedent=true -->
```csharp
string?[] values = ["a", null, "", " ", "b"];

// Remove null values, and get a sequence of non-nullable items
IEnumerable<string> notNull = values.WhereNotNull();
Assert.Equal(["a", "", " ", "b"], notNull);
Assert.Equal(["a", " ", "b"], values.WhereNotNullOrEmpty());
Assert.Equal(["a", "b"], values.WhereNotNullOrWhiteSpace());

// Treat a null sequence as an empty one
IEnumerable<int>? missing = null;
Assert.Empty(missing.EmptyIfNull());
```
<!-- endref -->

- `WhereNotNull` removes the `null` values of a sequence of reference types or of nullable value types, and returns a
  sequence of non-nullable items, so the compiler knows the items are not `null`.
- `WhereNotNullOrEmpty` and `WhereNotNullOrWhiteSpace` filter sequences of strings.
- The same three methods exist for `IAsyncEnumerable<T>`.
- `EmptyIfNull` returns an empty sequence for `null`, to loop over an optional collection without a check.

## Check a sequence

<!-- ref:../../tests/Meziantou.Framework.Tests/EnumerableTests.cs;region=EnumerableExtensions_Checks;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.True(new[] { 1, 2, 3 }.IsDistinct());
Assert.False(new[] { "a", "A" }.IsDistinct(StringComparer.OrdinalIgnoreCase));
Assert.False(new[] { (Id: 1, Name: "a"), (Id: 1, Name: "b") }.IsDistinctBy(item => item.Id));

Assert.Equal(1, new[] { "a", "b" }.IndexOf("b", StringComparer.Ordinal));
Assert.True(new[] { "Alpha", "Beta" }.ContainsIgnoreCase("beta"));

Assert.Equal(TimeSpan.FromMinutes(3), new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2) }.Sum());
```
<!-- endref -->

- `IsDistinct` and `IsDistinctBy` tell whether a sequence has duplicates, with an optional comparer. They stop at the
  first duplicate.
- `IndexOf` and `LongIndexOf` return the position of an item in any sequence, or -1.
- `ContainsIgnoreCase` looks for a string with an ordinal case-insensitive comparison.
- `Sum` and `Average` add up and average `TimeSpan` values.

## Update a collection

<!-- ref:../../tests/Meziantou.Framework.Tests/EnumerableTests.cs;region=EnumerableExtensions_Collections;format=md-fenced-code-block;dedent=true -->
```csharp
ICollection<int> numbers = new List<int> { 1, 2, 3, 4 };
numbers.AddRange(5, 6);
numbers.RemoveAll(number => number % 2 == 0);
Assert.Equal([1, 3, 5], numbers);

IList<string> names = ["a", "b"];
names.Replace("a", "A"); // Throws when the item is not in the list
names.AddOrReplace("c", "C"); // Adds the item when it is not in the list
Assert.Equal(["A", "b", "C"], names);
```
<!-- endref -->

- `AddRange` adds items to any `ICollection<T>`, and `RemoveAll` removes the items that match a predicate.
- `Replace` replaces an item of an `IList<T>`, and throws an `ArgumentOutOfRangeException` when it is not in the list.
  `AddOrReplace` adds the new item when the old one is not in the list.
- `EnqueueRange` adds several items to a `Queue<T>` or a `ConcurrentQueue<T>`.

## Update a dictionary

<!-- ref:../../tests/Meziantou.Framework.Tests/DictionaryExtensionsTests.cs;region=DictionaryExtensions_GetOrAdd;format=md-fenced-code-block;dedent=true -->
```csharp
var cache = new Dictionary<string, int>(StringComparer.Ordinal);
Assert.Equal(5, cache.GetOrAdd("hello", key => key.Length));
Assert.Equal(5, cache.GetOrAdd("hello", key => throw new InvalidOperationException("Not called")));

// TryUpdate only changes existing keys
Assert.True(cache.TryUpdate("hello", (key, value) => value * 2));
Assert.False(cache.TryUpdate("missing", 1));
Assert.Equal(10, cache["hello"]);
```
<!-- endref -->

`GetOrAdd` returns the value of a key, and adds it when the key is missing, from a value or a factory. The factory runs
only when the key is missing. `TryUpdate` changes the value of an existing key, and returns `false` when the key is
missing.

## Loop asynchronously

<!-- ref:../../tests/Meziantou.Framework.Tests/EnumerableTests.cs;region=EnumerableExtensions_ForEachAsync;format=md-fenced-code-block;dedent=true -->
```csharp
var processed = new ConcurrentBag<int>();

// One item at a time, in order
await new[] { 1, 2, 3 }.ForEachAsync(async item =>
{
    await Task.Yield();
    processed.Add(item);
});

// Up to 2 items at the same time
await new[] { 4, 5, 6 }.ParallelForEachAsync(degreeOfParallelism: 2, async item =>
{
    await Task.Yield();
    processed.Add(item);
});

Assert.Equal([1, 2, 3, 4, 5, 6], processed.Order());
```
<!-- endref -->

- `ForEachAsync` awaits the action for each item, one item after the other. Overloads give the action the index of the
  item and a `CancellationToken`.
- `ParallelForEachAsync` runs the action for several items at the same time, at most `degreeOfParallelism` at once
  (the number of processors by default). The items are not processed in order.
- `ForEach` is the synchronous loop, with or without the index.

`ToAsyncEnumerable` turns an `IEnumerable<T>` into an `IAsyncEnumerable<T>`, for an API that only accepts the latter.

## Enumerate once

An `IEnumerable<T>` can be expensive to enumerate: a database query, a file read line by line, a generator.
`CachedEnumerable.Create` enumerates the source once, the first time, and replays the items it has already read:

<!-- ref:../../tests/Meziantou.Framework.Tests/CachedEnumerableTests.cs;region=CachedEnumerable_Create;format=md-fenced-code-block;dedent=true -->
```csharp
var enumerationCount = 0;
IEnumerable<int> GetExpensiveEnumerable()
{
    enumerationCount++;
    yield return 1;
    yield return 2;
}

using var cached = CachedEnumerable.Create(GetExpensiveEnumerable());
Assert.Equal([1, 2], cached); // First enumeration
Assert.Equal([1, 2], cached); // Uses cached results
Assert.Equal(1, enumerationCount);
```
<!-- endref -->

- The cache is thread-safe by default. Pass `threadSafe: false` when only one thread enumerates it.
- The source is read lazily: an enumeration that stops early reads only the items it needs.
- Dispose the cached sequence to dispose the enumerator of the source. There is also an overload for
  `IAsyncEnumerable<T>`, which returns an `IAsyncDisposable`.

To find code that enumerates a sequence twice, wrap it with `AsEnumerableOnce`. A second enumeration throws:

<!-- ref:../../tests/Meziantou.Framework.Tests/EnumerableTests.cs;region=EnumerableExtensions_AsEnumerableOnce;format=md-fenced-code-block;dedent=true -->
```csharp
var once = new[] { 1, 2 }.AsEnumerableOnce();
Assert.Equal(3, once.Sum());

// A second enumeration throws, which reveals code that enumerates a sequence twice
Assert.Throws<InvalidOperationException>(() => once.Sum());
```
<!-- endref -->

`ToOnlyEnumerable` hides the type of a collection behind an iterator, so the code that receives it cannot cast it back to
a list and change it.

## Sort in parallel

`ParallelSort` and `ParallelStableSort` sort an array, a `List<T>` or a `Span<T>` on several threads, with an optional
comparer and degree of parallelism. `ParallelStableSort` keeps the order of equal items. They help with large
collections; for small ones, `Array.Sort` or `List<T>.Sort` is enough.
