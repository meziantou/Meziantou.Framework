---
title: Overview
description: Choose the collection that fits your data, from circular buffers to tries and equatable immutable collections.
---

The collections are in the `Meziantou.Framework.Collections` namespace. They complement the collections of .NET for
needs that come up regularly.

| Collection | Use it to | Page |
| --- | --- | --- |
| `CircularBuffer<T>` | Keep a fixed number of items, optionally overwriting the oldest ones | [Queues and buffers](02-QueuesAndBuffers.md) |
| `DoubleEndedQueue<T>` | Add and remove items at both ends, and read them by index | [Queues and buffers](02-QueuesAndBuffers.md) |
| `LimitList<T>` | Keep the most recent items, such as a list of recent files | [Queues and buffers](02-QueuesAndBuffers.md) |
| `AppendOnlyCollection<T>` | Add items from several threads while others read them | [Queues and buffers](02-QueuesAndBuffers.md) |
| `SortedList<T>` | Keep items sorted, duplicates included, with access by index | [Sorted collections](03-SortedCollections.md) |
| `SkipList<T>` | Keep unique items sorted | [Sorted collections](03-SortedCollections.md) |
| `BTree<T>` | Keep unique items sorted in a B-tree | [Sorted collections](03-SortedCollections.md) |
| `MultiValueDictionary<TKey, TValue>` | Associate several values with a key | [Dictionaries and tries](04-DictionariesAndTries.md) |
| `Trie<TValue>` | Find the keys that start with a prefix | [Dictionaries and tries](04-DictionariesAndTries.md) |
| `PruningRadixTree` | Suggest the most frequent completions of a prefix | [Dictionaries and tries](04-DictionariesAndTries.md) |
| `UnsafeListDictionary<TKey, TValue>` | Build a list of key-value pairs without checking for duplicates | [Dictionaries and tries](04-DictionariesAndTries.md) |
| `ImmutableEquatableArray<T>`, `ImmutableEquatableSet<T>`, `ImmutableEquatableDictionary<TKey, TValue>` | Compare collections by their content | [Equatable collections](05-EquatableCollections.md) |

The collections are not thread-safe, except `AppendOnlyCollection<T>` and the immutable equatable collections. For
thread-safe collections, see [concurrent collections](../../66-Threading/06-ConcurrentCollections.md) in
`Meziantou.Framework.Threading`.
