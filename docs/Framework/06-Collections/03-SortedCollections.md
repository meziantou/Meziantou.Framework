---
title: Sorted collections
description: Keep items sorted, with or without duplicates, and read them in order.
---

## Sorted list

[SortedList\<T\>](cref:Meziantou.Framework.Collections.SortedList{T}) keeps its items sorted as you add them, accepts duplicates, and gives access by index:

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

- [Add](cref:Meziantou.Framework.Collections.SortedList{T}.Add) inserts the item at its sorted position. Pass an `IComparer<T>` to the constructor to change the order.
- [AddRange](cref:Meziantou.Framework.Collections.SortedList{T}.AddRange) adds many items at once. Each call to `Add` moves the items that follow the new one, so prefer `AddRange` to add many items:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/SortedListTests.cs;region=SortedList_AddRange;format=md-fenced-code-block;dedent=true -->
```csharp
var list = new SortedList<int> { 5, 1 };

// Sorts the new items once, then merges them with the existing items
list.AddRange([4, 2, 5, 3]);

Assert.Equal([1, 2, 3, 4, 5, 5], list);
```
<!-- endref -->

- [BinarySearch](cref:Meziantou.Framework.Collections.SortedList{T}.BinarySearch), [IndexOf](cref:Meziantou.Framework.Collections.SortedList{T}.IndexOf), [FirstIndexOf](cref:Meziantou.Framework.Collections.SortedList{T}.FirstIndexOf) and [LastIndexOf](cref:Meziantou.Framework.Collections.SortedList{T}.LastIndexOf) find an item without scanning the list.
- [UnsafeAsReadOnlySpan](cref:Meziantou.Framework.Collections.SortedList{T}.UnsafeAsReadOnlySpan) returns the items as a span, without copying them. The span is valid until the list changes.

Not to be confused with `System.Collections.Generic.SortedList<TKey, TValue>`, which is a dictionary.

## Skip list

[SkipList\<T\>](cref:Meziantou.Framework.Collections.SkipList{T}) keeps unique items sorted:

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

- [Add](cref:Meziantou.Framework.Collections.SkipList{T}.Add) returns `false` when an equal item is already present, for the comparer of the list.
- [TryGetValue](cref:Meziantou.Framework.Collections.SkipList{T}.TryGetValue) returns the stored item that is equal to the one you pass, for instance to get its original case.
- [Remove](cref:Meziantou.Framework.Collections.SkipList{T}.Remove) removes an item; the constructors can take an initial collection.

## B-tree

[BTree\<T\>](cref:Meziantou.Framework.Collections.BTree{T}) also keeps unique items sorted, in a B-tree:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/BTreeTests.cs;region=BTree_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = new BTree<int>();
Assert.True(tree.Add(30));
Assert.True(tree.Add(10));
Assert.True(tree.Add(20));
Assert.False(tree.Add(10)); // Already present

Assert.True(tree.Contains(20));
Assert.Equal([10, 20, 30], tree);

Assert.True(tree.Remove(20));
Assert.False(tree.Remove(20)); // Not present anymore
Assert.Equal([10, 30], tree);
```
<!-- endref -->

- [Add](cref:Meziantou.Framework.Collections.BTree{T}.Add) returns `false` when the item is already present, and [Remove](cref:Meziantou.Framework.Collections.BTree{T}.Remove) returns `false` when it is missing.
- [Clear](cref:Meziantou.Framework.Collections.BTree{T}.Clear) removes all the items.
- Enumerating the tree returns the items in order. Changing the tree while enumerating it makes the enumerator throw an `InvalidOperationException`.
