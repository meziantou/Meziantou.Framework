---
title: Sorted collections
description: Keep items sorted, with or without duplicates, and read them in order.
---

## Sorted list

`SortedList<T>` keeps its items sorted as you add them, accepts duplicates, and gives access by index:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/SortedListTests.cs;region=SortedList_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var scores = new SortedList<int>();
scores.Add(42);
scores.Add(7);
scores.Add(42);
scores.Add(19);

// Items are kept sorted, duplicates included, and can be read by index
Assert.Equal([7, 19, 42, 42], scores);
Assert.Equal(7, scores[0]);
Assert.Equal(2, scores.FirstIndexOf(42));
Assert.Equal(3, scores.LastIndexOf(42));
```
<!-- endref -->

- `Add` inserts the item at its sorted position. Pass an `IComparer<T>` to the constructor to change the order.
- `BinarySearch`, `IndexOf`, `FirstIndexOf` and `LastIndexOf` find an item without scanning the list.
- `UnsafeAsReadOnlySpan` returns the items as a span, without copying them. The span is valid until the list changes.

Not to be confused with `System.Collections.Generic.SortedList<TKey, TValue>`, which is a dictionary.

## Skip list

`SkipList<T>` keeps unique items sorted:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/SkipListTests.cs;region=SkipList_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var tags = new SkipList<string>(StringComparer.OrdinalIgnoreCase);
Assert.True(tags.Add("beta"));
Assert.True(tags.Add("alpha"));

// Values are unique for the comparer
Assert.False(tags.Add("ALPHA"));
Assert.Equal(["alpha", "beta"], tags);

// Get the stored value that is equal to another one
Assert.True(tags.TryGetValue("BETA", out var stored));
Assert.Equal("beta", stored);
```
<!-- endref -->

- `Add` returns `false` when an equal item is already present, for the comparer of the list.
- `TryGetValue` returns the stored item that is equal to the one you pass, for instance to get its original case.
- `Remove` removes an item; the constructors can take an initial collection.

## B-tree

`BTree<T>` also keeps unique items sorted, in a B-tree:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/BTreeTests.cs;region=BTree_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = new BTree<int>();
Assert.True(tree.Add(30));
Assert.True(tree.Add(10));
Assert.True(tree.Add(20));
Assert.False(tree.Add(10)); // Already present

Assert.True(tree.Contains(20));
Assert.Equal([10, 20, 30], tree);
```
<!-- endref -->

`Add` returns `false` when the item is already present. Enumerating the tree returns the items in order.
