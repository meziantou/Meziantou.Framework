---
title: Queues and buffers
description: Keep a fixed number of items, add and remove items at both ends, keep the most recent items, or append from several threads.
---

## Circular buffer

[CircularBuffer\<T\>](cref:Meziantou.Framework.Collections.CircularBuffer{T}) holds at most [Capacity](cref:Meziantou.Framework.Collections.CircularBuffer{T}.Capacity) items. With [AllowOverwrite](cref:Meziantou.Framework.Collections.CircularBuffer{T}.AllowOverwrite), adding to a full buffer removes the item at the
other end:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/CircularBufferTests.cs;region=CircularBuffer_AllowOverwrite;format=md-fenced-code-block;dedent=true -->
```csharp
var buffer = new CircularBuffer<int>(3) { AllowOverwrite = true };
buffer.AddLast(1);
buffer.AddLast(2);
buffer.AddLast(3);
Assert.Equal([1, 2, 3], buffer);

buffer.AddLast(4); // overwrites 1
Assert.Equal([2, 3, 4], buffer);
```
<!-- endref -->

Without [AllowOverwrite](cref:Meziantou.Framework.Collections.CircularBuffer{T}.AllowOverwrite), adding to a full buffer throws an `InvalidOperationException`:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/CircularBufferTests.cs;region=CircularBuffer_Full;format=md-fenced-code-block;dedent=true -->
```csharp
var buffer = new CircularBuffer<int>(2);
buffer.AddLast(1);
buffer.AddLast(2);

// Without AllowOverwrite, adding to a full buffer throws
Assert.Throws<InvalidOperationException>(() => buffer.AddLast(3));

// Remove items from either end
Assert.Equal(1, buffer.RemoveFirst());
buffer.AddLast(3);
Assert.Equal([2, 3], buffer);
```
<!-- endref -->

- [AddLast](cref:Meziantou.Framework.Collections.CircularBuffer{T}.AddLast) and [AddFirst](cref:Meziantou.Framework.Collections.CircularBuffer{T}.AddFirst) add an item at one end. When the buffer is full and [AllowOverwrite](cref:Meziantou.Framework.Collections.CircularBuffer{T}.AllowOverwrite) is `true`, [AddLast](cref:Meziantou.Framework.Collections.CircularBuffer{T}.AddLast)
  overwrites the first item and [AddFirst](cref:Meziantou.Framework.Collections.CircularBuffer{T}.AddFirst) the last one.
- [RemoveFirst](cref:Meziantou.Framework.Collections.CircularBuffer{T}.RemoveFirst) and [RemoveLast](cref:Meziantou.Framework.Collections.CircularBuffer{T}.RemoveLast) remove and return an item, and throw an `InvalidOperationException` when the buffer is
  empty.
- The indexer reads an item by its position, from the first item. You can change [Capacity](cref:Meziantou.Framework.Collections.CircularBuffer{T}.Capacity) after the buffer is created, to any
  value greater than 0 that holds the current items.
- [CopyTo](cref:Meziantou.Framework.Collections.CircularBuffer{T}.CopyTo) checks that the destination array has room for all the items before it writes any of them.

Use it for the last N log lines, measurements or events.

## Double-ended queue

[DoubleEndedQueue\<T\>](cref:Meziantou.Framework.Collections.DoubleEndedQueue{T}) grows as needed, and adds and removes items at both ends:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/DoubleEndedQueueTests.cs;region=DoubleEndedQueue_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var queue = new DoubleEndedQueue<string>(capacity: 4); // Grows when needed
queue.AddLast("b");
queue.AddLast("c");
queue.AddFirst("a");

Assert.Equal(["a", "b", "c"], queue);
Assert.Equal("c", queue[2]);
Assert.Equal("a", queue.RemoveFirst());
Assert.Equal("c", queue.RemoveLast());
```
<!-- endref -->

The constructor takes the initial capacity. [RemoveFirst](cref:Meziantou.Framework.Collections.DoubleEndedQueue{T}.RemoveFirst) and [RemoveLast](cref:Meziantou.Framework.Collections.DoubleEndedQueue{T}.RemoveLast) throw an `InvalidOperationException` when the
queue is empty.

## Most recent items

[LimitList\<T\>](cref:Meziantou.Framework.Collections.LimitList{T}) keeps at most a number of items, and drops the item at the other end when you add one to a full list:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/LimitListTests.cs;region=LimitList_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Keep the 3 most recent items
var recentFiles = new LimitList<string>(3);
recentFiles.AddFirst("a.txt");
recentFiles.AddFirst("b.txt");
recentFiles.AddFirst("c.txt");
recentFiles.AddFirst("d.txt"); // a.txt, the last item, is removed

Assert.Equal(["d.txt", "c.txt", "b.txt"], recentFiles);
```
<!-- endref -->

[AddFirst](cref:Meziantou.Framework.Collections.LimitList{T}.AddFirst) drops the last item, and [AddLast](cref:Meziantou.Framework.Collections.LimitList{T}.AddLast) drops the first one. Unlike [CircularBuffer\<T\>](cref:Meziantou.Framework.Collections.CircularBuffer{T}), a full [LimitList\<T\>](cref:Meziantou.Framework.Collections.LimitList{T})
never throws. It also supports `Remove`, [RemoveAt](cref:Meziantou.Framework.Collections.LimitList{T}.RemoveAt), [RemoveFirst](cref:Meziantou.Framework.Collections.LimitList{T}.RemoveFirst), [RemoveLast](cref:Meziantou.Framework.Collections.LimitList{T}.RemoveLast) and setting an item by index.

[LimitList\<T\>](cref:Meziantou.Framework.Collections.LimitList{T}) implements `IList<T>` and `IReadOnlyList<T>`. It stores the items in a ring buffer, so reading an item by index and adding or
removing an item at either end take constant time. [Insert](cref:Meziantou.Framework.Collections.LimitList{T}.Insert) on a full list drops the last item, except at index [Count](cref:Meziantou.Framework.Collections.LimitList{T}.Count), where it
behaves like [AddLast](cref:Meziantou.Framework.Collections.LimitList{T}.AddLast) and drops the first one. `foreach` uses a struct enumerator, so it does not allocate.

## Append-only collection

[AppendOnlyCollection\<T\>](cref:Meziantou.Framework.Collections.AppendOnlyCollection{T}) is a collection you can only add to. Adding is thread-safe, and reading — the indexer,
[Count](cref:Meziantou.Framework.Collections.AppendOnlyCollection{T}.Count), enumeration, [Contains](cref:Meziantou.Framework.Collections.AppendOnlyCollection{T}.Contains), [Find](cref:Meziantou.Framework.Collections.AppendOnlyCollection{T}.Find) and [TryFind](cref:Meziantou.Framework.Collections.AppendOnlyCollection{T}.TryFind) — needs no lock, even while other threads add items:

<!-- ref:../../../tests/Meziantou.Framework.Tests/Collections/AppendOnlyCollectionTests.cs;region=AppendOnlyCollection_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var log = new AppendOnlyCollection<string>();

// Add items from several threads, and read them at the same time
await Task.WhenAll(
    Task.Run(() => log.Add("from task 1")),
    Task.Run(() => log.Add("from task 2")));

Assert.HasCount(2, log);
Assert.True(log.Contains(item => item == "from task 1"));
Assert.Equal("from task 2", log.Find(item => item.EndsWith("2", StringComparison.Ordinal)));
```
<!-- endref -->

Items cannot be removed: `Clear` and `Remove` throw a `NotSupportedException`.
