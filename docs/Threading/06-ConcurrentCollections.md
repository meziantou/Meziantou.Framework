---
title: Concurrent collections
description: Share a set or a list between threads with ConcurrentHashSet and SynchronizedList, and run several list operations atomically.
---

The collections are in the `Meziantou.Framework.Collections.Concurrent` namespace. To bind a collection to a UI control,
see [Observable collection](07-ObservableCollection.md).

## A thread-safe set with ConcurrentHashSet

`ConcurrentHashSet<T>` is a set of unique values that several threads can read and modify at the same time:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/ConcurrentHashSetTests.cs;region=ConcurrentHashSet_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var set = new ConcurrentHashSet<int>();
set.Add(1);
set.Add(2);
Assert.Contains(1, set);
```
<!-- endref -->

Pass an `IEqualityComparer<T>` to the constructor to decide which values are equal. `Add` returns `false` when the set
already contains an equal value, which makes it a thread-safe "first time I see this value" check:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/ConcurrentHashSetTests.cs;region=ConcurrentHashSet_Comparer;format=md-fenced-code-block;dedent=true -->
```csharp
var set = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);

// Add returns false when the set already contains an equal value
Assert.True(set.Add("Alice"));
Assert.False(set.Add("ALICE"));

set.AddRange("Bob", "Charlie");
Assert.HasCount(3, set);
Assert.True(set.Remove("bob"));
```
<!-- endref -->

- `ConcurrentHashSet<T>` implements `ISet<T>` and `IReadOnlySet<T>`: `UnionWith`, `IntersectWith`, `ExceptWith`,
  `SymmetricExceptWith`, and the comparison methods are available.
- The comparison methods (`IsSubsetOf`, `IsSupersetOf`, `Overlaps`, `SetEquals` and the proper variants) do not take a
  snapshot of the set. When other threads modify the set while they run, their result is only an approximation.
- `IsEmpty` and `Count` give the state at the time of the call.

## A thread-safe list with SynchronizedList

`SynchronizedList<T>` is a list whose members are each protected by a lock. Enumerating it enumerates a copy taken at the
start of the enumeration, so other threads can modify the list meanwhile:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/SynchronizedListTests.cs;region=SynchronizedList_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var list = new SynchronizedList<int>();
list.Add(1);
list.Add(2);
Assert.Equal([1, 2], list); // Enumerating the list enumerates a snapshot

// Multiple operations as a single atomic unit
list.Execute(items =>
{
    if (!items.Contains(3))
    {
        items.Add(3);
    }
});
Assert.Equal([1, 2, 3], list);
```
<!-- endref -->

Each member is atomic, but a sequence of members is not: `if (!list.Contains(item)) list.Add(item);` can add the item
twice, and reading `list[list.Count - 1]` can throw when another thread removes an item in between. Run such sequences in
`Execute`, which holds the lock while your code works on the underlying `List<T>`. `Execute` can also return a value:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/SynchronizedListTests.cs;region=SynchronizedList_ExecuteWithResult;format=md-fenced-code-block;dedent=true -->
```csharp
var list = new SynchronizedList<string>(["a", "b"]);

// Read and update the list atomically, and return a value
var removed = list.Execute(items =>
{
    var last = items[^1];
    items.RemoveAt(items.Count - 1);
    return last;
});

Assert.Equal("b", removed);
Assert.Equal(["a"], list);
```
<!-- endref -->

- Keep the code passed to `Execute` short, and do not block or acquire other locks in it: every other member of the list
  waits while it runs.
- Each enumeration copies the whole list. Avoid enumerating a large list in a hot path.
- The constructors accept an initial capacity, a sequence or a span of items. `EnsureCapacity` grows the underlying list.
